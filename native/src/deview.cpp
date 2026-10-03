/*
 * DiffEngineViewer native renderer: raylib for the window and GL, Dear ImGui for the widgets.
 *
 * The ImGui backend here is deliberately minimal. Panes do not scroll inside ImGui, because the
 * managed side already slices each frame to the visible rows, so there is no scroll state to keep
 * in sync and no keyboard navigation to wire up. That leaves only three things a backend must do:
 * honour texture requests, feed mouse input, and turn ImDrawData into rlgl calls.
 */
#include "deview.h"

#include "imgui.h"
/* For ScrollbarEx and ImRect. Internal, but it is the only way to put ImGui's own scrollbar
 * somewhere other than the edge of a window it is itself scrolling, and this one scrolls a model
 * that lives in another process. imgui is pinned by tag in CMakeLists.txt, so the coupling moves
 * only when someone moves it. */
#include "imgui_internal.h"
#include "raylib.h"
#include "rlgl.h"

#include <algorithm>
#include <cfloat>
#include <climits>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <filesystem>
#include <fstream>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <system_error>
#include <thread>
#include <utility>
#include <vector>

/* For fontconfig, which is found at run time rather than linked: see Fontconfig. */
#if !defined(_WIN32)
#include <dlfcn.h>
#endif

/*
 * raylib latches GLFW's close flag and exposes no way to clear it, but the window has to survive a
 * close when a tray is running, otherwise every later frame would report closing again. raylib
 * statically links GLFW into this library so the symbols resolve, and glfwGetCurrentContext
 * returns raylib's own window without needing the GLFW headers.
 */
extern "C" void* glfwGetCurrentContext(void);
extern "C" void glfwSetWindowShouldClose(void* window, int value);

/*
 * And GLFW's callback for the window system wanting a window's content drawn again, because part
 * of it has been uncovered or it has been put back on the screen. raylib asks to be told when a
 * window is resized, moved, minimised or focused and not this, since it draws every frame whatever
 * happens. A window that has stopped drawing frames nobody needs has to hear it: see State::stale.
 * Setting it here takes nothing from raylib, which sets none.
 */
extern "C"
{
typedef void (*DeviewRefresh)(void* window);
DeviewRefresh glfwSetWindowRefreshCallback(void* window, DeviewRefresh callback);
}

/*
 * And its callbacks for a mouse button, the wheel, a key and a character. raylib sets all four, and
 * keeps from them what was so when it last read the window system's events: whether a button is
 * down, the last wheel message, which keys are down. That is a state, read once a frame, and a
 * press and a release that arrive between two reads leave it as it was. So these are set over
 * raylib's, which are kept and still called, and what they are told is kept as what happened: see
 * State::presses.
 *
 * And the one for the pointer crossing the window's edge, which raylib also keeps and nothing
 * else here could be told by: where the pointer is stays where it last was in the window, for as
 * long as it is anywhere else. See State::pointerInside.
 */
extern "C"
{
typedef void (*DeviewButtonEvent)(void* window, int button, int action, int mods);
typedef void (*DeviewScrollEvent)(void* window, double across, double down);
typedef void (*DeviewKeyEvent)(void* window, int key, int scancode, int action, int mods);
typedef void (*DeviewCharacterEvent)(void* window, unsigned int codepoint);
typedef void (*DeviewCrossingEvent)(void* window, int entered);
DeviewCrossingEvent glfwSetCursorEnterCallback(void* window, DeviewCrossingEvent callback);
DeviewButtonEvent glfwSetMouseButtonCallback(void* window, DeviewButtonEvent callback);
DeviewScrollEvent glfwSetScrollCallback(void* window, DeviewScrollEvent callback);
DeviewKeyEvent glfwSetKeyCallback(void* window, DeviewKeyEvent callback);
DeviewCharacterEvent glfwSetCharCallback(void* window, DeviewCharacterEvent callback);

/* What a key types on the layout in use, unshifted, or null for a key that types nothing. The
 * key is one of GLFW's numbers, or -1 for the key with that scancode. */
const char* glfwGetKeyName(int key, int scancode);
}

/*
 * And GLFW's way to an entry point of the GL the window was made with, for the one thing asked of
 * GL that rlgl has no call for: how large a texture it will take. See TextureLimit.
 */
extern "C"
{
typedef void (*DeviewGlEntry)(void);
DeviewGlEntry glfwGetProcAddress(const char* name);
}

/*
 * And how much larger than a pixel the desktop wants everything drawn, which under X11 is the
 * Xft.dpi resource over 96. raylib asks GLFW this only for a window it was told to scale itself,
 * which this one is not: see State::scale.
 */
extern "C" void glfwGetWindowContentScale(void* window, float* across, float* down);

/* GLFW's own numbers, which its headers would have named. */
constexpr int glfwRelease = 0;
constexpr int glfwShift = 0x0001;
constexpr int glfwControl = 0x0002;
constexpr int glfwSuper = 0x0008;

namespace
{
void ClearCloseFlag()
{
    void* handle = glfwGetCurrentContext();
    if (handle != nullptr)
    {
        glfwSetWindowShouldClose(handle, 0);
    }
}

/*
 * Queue column widths, counted in character cells rather than pixels so a scaled display gets a
 * column that holds the same number of characters rather than a narrower one.
 */
constexpr float queueCells = 34.0f;
constexpr float minQueueCells = 8.0f;

/*
 * What the drag leaves each of the two panes, so the splitter cannot be pushed far enough right to
 * squeeze them out of existence.
 */
constexpr float minPaneCells = 12.0f;

/*
 * How far either side of the divider counts as grabbing it. The border is a single pixel, which is
 * not something a mouse can be asked to hit.
 */
constexpr float grabWidth = 4.0f;

/*
 * deview_init's fontSize is an em size, which is what Core Text and GDI+ take and therefore what
 * the other two heads render at. ImGui's stb_truetype loader scales by pixel height instead
 * (stbtt_ScaleForPixelHeight in imgui_draw.cpp), so the same 15 came out as an em of about 11 and
 * text a quarter smaller than the other heads, which is what left this head's queue column holding
 * 34 characters in far fewer pixels.
 *
 * The correction is the font's own ascent plus descent over its em, and it is a constant because
 * the only font that reaches here is the JetBrains Mono the managed side embeds: 1020 and 300 over
 * 1000 units. Swapping that font means revisiting this number, hence naming it rather than folding
 * it into the size. The machine's fonts, merged in for the characters that one lacks, do not come
 * through here, and have theirs read out of their own tables: see EmScaleOf.
 */
constexpr float emScale = 1.32f;

/*
 * The side of a checker square behind a picture, so an image with transparency reads as transparent
 * rather than as whatever colour the pane happens to be. Matches the WinForms head.
 */
constexpr float checkerSize = 8.0f;

/*
 * How long a frame lasts, which is what holds the managed loop to sixty turns a second: it calls
 * deview_present as fast as that returns. raylib used to do the waiting, inside EndDrawing. It is
 * done here now, because a frame that is not drawn has to be waited out as well: see Rest.
 */
constexpr double frameSeconds = 1.0 / 60.0;

/*
 * How many frames in a row have to be built with nothing arriving, and come out as the frame on
 * the screen, before frames stop being built: a second of them, since each is waited out.
 *
 * ImGui does things over several frames and counts some of them in the time it is told has passed.
 * A layout can take a second frame to settle, input given in one frame may be acted on over the
 * next few, and for a quarter of a second after the pointer leaves a row with a tooltip the next
 * row's comes up without its delay. A second is longer than any of them, and costs little: these
 * are frames that are built and compared, not drawn.
 */
constexpr int settledFrames = 60;

/*
 * One decoded picture, kept because BuildFrame runs sixty times a second and decoding an image per
 * frame is what turns a window that is merely showing something into one that is busy.
 *
 * A false `loaded` once `decoding` is over is a remembered failure. raylib is built here with
 * decoders for PNG, JPEG, BMP and GIF and has none for WebP or ICO, so a pane can legitimately carry
 * a path this build cannot read; remembering that means attempting it once rather than once a frame.
 * Nothing is lost when it happens — the rows already say what the file is, and they are the
 * description an image comparison is made of.
 */
struct CachedTexture
{
    Texture2D texture{};
    bool loaded = false;

    /* Being decoded on the decoder's thread. The pane shows a spinner until it lands. */
    bool decoding = false;

    std::uintmax_t length = 0;
    std::filesystem::file_time_type written{};

    /* Whether the frame being built asked for this picture. What ForgetUnusedPictures keeps. */
    bool used = false;

    /* How it is sampled now, which is by how it was last drawn: see Sample. */
    int sampling = 0;

    /* Some of it can be seen through, so it is drawn over a checkerboard: see SeeThrough. */
    bool translucent = false;
};

/*
 * One picture to decode, or decoded: the path, the stamp the decode was asked for, and once it is
 * done the pixels, which are empty when raylib could not read the file, and whether any of them
 * can be seen through.
 */
struct Decode
{
    std::string path;
    std::uintmax_t length = 0;
    std::filesystem::file_time_type written{};
    Image image{};
    bool translucent = false;
};

/*
 * Pictures are decoded on a thread of their own. Decoded on this one, a picture held the window for
 * as long as it took, and a page of a document or a large screenshot takes tens of milliseconds and
 * more. Only the decode moves: uploading the pixels is a GL call, which belongs to the thread that
 * owns the context, and is the cheap half.
 *
 * Shared with that thread through a shared_ptr, and the thread detached rather than joined: a
 * process that exits without deview_shutdown would otherwise destroy a joinable std::thread, which
 * terminates the process.
 */
struct Decoder
{
    std::mutex mutex;
    std::condition_variable wake;
    std::deque<Decode> requests;
    std::vector<Decode> done;
    bool stopping = false;

    /* The longest side of a texture the window's GL takes, read on its thread before this one
     * was started: see TextureLimit. */
    int textureLimit = 0;
};

/*
 * One of the machine's fonts, read whole, for the characters the embedded one does not have:
 * its bytes, which of the faces in them, and the scale that makes deview_init's size an em for
 * that face, as emScale does for the embedded font.
 */
struct FoundFont
{
    std::vector<unsigned char> data;
    int face = 0;
    float scale = 1.0f;
};

/*
 * Those fonts are looked for and read on a thread of their own, for the reason pictures are
 * decoded on one: fontconfig reading its caches and a CJK collection coming off the disk are tens
 * of milliseconds each, and the first is seconds on a machine whose caches are stale. Shared and
 * detached as the decoder is, and for its reason.
 */
struct FontFinder
{
    std::mutex mutex;
    std::condition_variable wake;

    /* Characters the window's font cannot draw, each asked about once. */
    std::vector<uint32_t> wanted;
    std::vector<FoundFont> found;
    bool stopping = false;
};

struct State
{
    bool initialised = false;
    bool windowOpen = false;
    ImGuiContext* context = nullptr;
    DeviewInput input{};

    /*
     * The font the window draws with: the embedded font, and merged into it whichever of the
     * machine's fonts a character on screen has needed.
     *
     * Not the font a capture draws with. That is the embedded font alone, which the atlas holds
     * a second time and ahead of this one, so that a capture is the same picture on a machine
     * with every font installed and on one with none: a character the embedded font lacks is the
     * replacement glyph there, as it was everywhere before this. What the two draw from the
     * embedded font is the same glyphs, so the captures still describe the window.
     */
    ImFont* font = nullptr;

    /* Started with the first character the font cannot draw, so a window that never shows one
     * never has it, nor fontconfig. */
    std::shared_ptr<FontFinder> finder;

    /* One bit a code point, set once it has been looked at, so it is asked about once and a
     * frame of text already seen costs a pass over its bytes. Empty until something past ASCII
     * turns up. */
    std::vector<bool> asked;

    /* The bytes of each font merged in. ImGui rasterises a glyph out of them when a character is
     * first drawn, so they are kept for as long as the atlas is. */
    std::deque<std::vector<unsigned char>> fontData;

    /*
     * How much larger than a pixel the desktop wants things drawn: 1 on an ordinary display, 2 on
     * one whose desktop is set to twice the size, and anything between.
     *
     * Nothing here asked. Under X11 a pixel is a pixel whatever the display, so the window was
     * 1100 by 700 of them and its text 15 to the em on a display with twice as many to the inch,
     * where everything else on the desktop is twice that: the viewer at half size.
     *
     * The window is drawn larger rather than handed to raylib to scale, which it would do by
     * drawing the same frame through a transform: the text here is rasterised at the size it is
     * shown, and everything stays in the pixels the pointer is reported in, so nothing that is
     * hit tested has two sets of coordinates to keep apart. What is scaled is the font, ImGui's
     * paddings and spacings, the few lengths this file gives in pixels, and the size a window
     * opens at when there is none remembered. A remembered one is in pixels already.
     *
     * Read once, as the window is made: GLFW works it out as it starts and keeps it. Never applied
     * to a capture, which draws at the size it is told at a scale of 1, on any display.
     */
    float scale = 1.0f;

    /* A character cell's width and a row's height in the last frame built for the window, which
     * is what the grid reported to the managed side is counted in: see MeasureGrid. */
    float cellWidth = 0.0f;
    float lineHeight = 0.0f;

    /* Whether the last screen carried a context menu, which is what makes Escape and a click
     * outside it a dismissal rather than what they would otherwise mean. */
    bool menuOpen = false;

    /* What a wheel message left over. A notch is 1.0, and a touchpad sends fractions of one:
     * truncating each frame's value on its own threw all of them away, so a touchpad scrolled
     * nothing at all. */
    float scrollRemainder = 0.0f;

    /*
     * What the pointer's buttons, the wheel and the keys have done since each was last taken, as
     * GLFW reported it: every press and release in the order they came, every wheel message added
     * up, every key pressed with the character it typed.
     *
     * Read as a state once a frame, which is how raylib offers them, a press and a release that
     * arrived together had never happened, and of several wheel messages only the last had. A tap
     * on a touchpad is such a press, and so is every click xdotool sends: under Xvfb none of ten
     * clicks was seen, and three of ten presses of Page Down.
     *
     * The presses are ImGui's, handed over at the top of the next frame built. It takes a press and
     * a release handed over together a frame apart, so what is drawn is clicked as it would be by a
     * button that was held. Nothing else here asks raylib about a button, so there is no second
     * account of one to disagree with ImGui's.
     */
    struct Press
    {
        int button;
        bool down;
    };

    std::vector<Press> presses;
    bool held[3] = {};

    /* The wheel twice over, because it is taken twice: by ImGui with the presses, and by
     * deview_poll_input for the managed side, which is not at the same moment. */
    float wheelAcross = 0.0f;
    float wheelDown = 0.0f;
    float wheelNotches = 0.0f;

    /*
     * A key pressed, or repeating while it is held, with the character it typed if it typed one.
     * Handed to the managed side one a poll, as the other two heads hand theirs, so two keys
     * between two polls are both acted on and in their order.
     */
    struct KeyPress
    {
        /* GLFW's number for the key, which is where it is on a US keyboard, or -1 for a key it
         * has no number for, and the window system's own number for it. */
        int key;
        int scancode;
        int mods;
        bool repeated;
        unsigned int character;
    };

    std::deque<KeyPress> keys;

    /* Whether the next character GLFW reports was typed by the key press at the back of the queue,
     * which is so only straight after that press: GLFW reports the two together. */
    bool characterFollows = false;

    /* A key has been pressed since Arrived last asked. */
    bool keyed = false;

    /*
     * Whether the pointer is over the window, by the last crossing GLFW reported, and whether the
     * last frame was built with it gone.
     *
     * ImGui was told where the pointer is on every frame, and raylib goes on answering with the
     * last place it was in the window. So a pointer that left over a queue row was still on that
     * row: it stayed lit, and its tooltip came up with the pointer on another window. A pointer
     * that has left is now reported to ImGui as nowhere, which is what it has for that.
     *
     * Not while a button is held. The window system goes on reporting a pointer that was pressed
     * in the window wherever it is taken, and a selection dragged past the window's edge has to
     * go on being one.
     *
     * Taken to be inside until a crossing says otherwise, so a window that opens under the pointer
     * and is told of no crossing is no worse off than it was.
     */
    bool pointerInside = true;
    bool pointerGone = false;

    /* raylib's callbacks, which go on being called. */
    DeviewCrossingEvent raylibCrossing = nullptr;
    DeviewButtonEvent raylibButton = nullptr;
    DeviewScrollEvent raylibScroll = nullptr;
    DeviewKeyEvent raylibKey = nullptr;
    DeviewCharacterEvent raylibCharacter = nullptr;

    /*
     * The queue column, owned here rather than by the table.
     *
     * ImGuiTableFlags_Resizable would give the drag for free, but it also hands the width to
     * ImGui's own table state, which initialises once and then auto-fits or restores from saved
     * settings. A column that is fixed and not resizable takes InitStretchWeightOrWidth on every
     * frame instead, which is a width this side decides and can therefore reproduce. The pixel
     * captures take this further: each draws its one frame in a fresh context (see
     * deview_capture), so nothing ImGui carries between frames can make a capture depend on the
     * capture before it.
     */
    float queueWidth = 0.0f;

    /* What was last handed to raylib, so an idle frame is not a window system call. */
    int cursor = MOUSE_CURSOR_DEFAULT;

    /* Keyed by the path the screen model handed over. std::map rather than unordered, because the
     * entries are handed out as pointers and this one does not move them. */
    std::map<std::string, CachedTexture> pictures;

    /* Started with the first picture asked for, so a window that never shows one never has it. */
    std::shared_ptr<Decoder> decoder;

    /*
     * The checkerboard behind a picture that can be seen through: two squares by two, a texel
     * each, which the picture's texture coordinates repeat across it. Made with the first picture
     * to need it, and once, whether or not that worked: see Checker.
     */
    Texture2D checker{};
    bool checkerMade = false;

    /* Inside deview_capture, which draws one frame that has to come out the same every time: its
     * pictures are decoded there and then, and a spinner stands still. */
    bool capturing = false;

    /*
     * A text selection being dragged out: whether the button is still down, which pane it went
     * down in, and where. The side is fixed for the life of the drag, because a selection belongs
     * to one pane and the other one's rows are a different document.
     *
     * The anchor is kept here rather than reported once, because the managed side takes both ends
     * of a drag on every frame it is held. That is what makes a press and release landing inside
     * a single frame arrive whole.
     */
    bool dragging = false;
    int32_t dragSide = -1;
    int32_t dragAnchorRow = 0;
    int32_t dragAnchorColumn = 0;

    /*
     * Where the last frame put each pane's picture, or the spinner standing in for one: what a
     * wheel notch and a press are resolved against. Left as they were by a capture, which draws at
     * a size of its own.
     */
    struct PictureSpace
    {
        bool present = false;
        float left = 0.0f;
        float top = 0.0f;
        float width = 0.0f;
        float height = 0.0f;

        /* Enlarged past the space, so there is somewhere for a drag to take it: the whole picture's
         * size as drawn, the centre it was drawn about, and how much of it shows each way. */
        bool enlarged = false;
        float wholeWidth = 0.0f;
        float wholeHeight = 0.0f;
        float centreX = 0.5f;
        float centreY = 0.5f;
        float across = 1.0f;
        float down = 1.0f;

        /* Whether there is more of it across, and down, than the space shows: whether the space
         * cut it short that way, which is the only way it can be moved. */
        bool movesAcross = false;
        bool movesDown = false;
    };

    PictureSpace pictureSpaces[2];

    /*
     * An enlarged picture being dragged: where the button went down, and how the picture was placed
     * then, which the whole drag is measured from. Measured from the last frame instead, a drag
     * would drift by whatever each frame's clamp took off it.
     */
    bool panning = false;
    int32_t panSide = 0;
    ImVec2 panStart{};
    PictureSpace panFrom{};

    /*
     * Where the right-click that asked for a pane's menu landed, which is where the menu hangs: the
     * managed side knows which pane and nothing of where in it. And where the menu was last drawn,
     * so a right-click on the menu itself is not taken for one on the pane under it.
     */
    ImVec2 paneMenuAnchor{};
    ImVec2 menuMin{};
    ImVec2 menuMax{};

    /* How the last window was left, handed over before this one exists and used as it is made. */
    bool placed = false;
    DeviewPlacement placement{};

    /*
     * The window's bounds as they were when it was last neither maximised nor minimised, and
     * whether it is maximised now. GLFW says where a maximised window is and nothing about where
     * it will go back to, so that has to have been noted while it was still there.
     */
    bool tracked = false;
    DeviewPlacement normal{};

    /*
     * What decides whether a frame is put on the screen, and whether one is built at all: see
     * deview_present. First the screen as it was last handed over, every byte of it, and the one
     * being held against it.
     */
    std::vector<unsigned char> presented;
    std::vector<unsigned char> arriving;

    /* What was drawn to make the frame on the screen, reduced to a number: see Fingerprint. */
    uint64_t shown = 0;

    /*
     * The window cannot be taken to be showing the frame last drawn into it: nothing has been
     * drawn yet, the window system has asked for its content again, it has changed size or come
     * back from being hidden, or a texture has been put behind a name that frame may have used.
     * The next frame is drawn, whatever it comes out as.
     */
    bool stale = true;

    /* Frames in a row that had nothing arrive and came out as the one on the screen. */
    int settled = 0;

    /* A queue row's tooltip is waiting out its delay, which ImGui counts in the frames it is
     * given: see BuildFrame. */
    bool tooltipDue = false;

    /* When the last present began, and when the last frame's wait ended, by GetTime. */
    double began = 0.0;
    double ended = 0.0;

    /* The pointer and the window as the last present found them. */
    Vector2 pointer{};
    int width = 0;
    int height = 0;
    bool hidden = false;
    bool minimised = false;
    bool focused = false;

    /*
     * The file behind each picture the last frame built for the window asked for, as that frame
     * found it: there or not, and if there, written when and how long. See PicturesRewritten.
     */
    struct Watched
    {
        std::string path;
        bool there = false;
        std::filesystem::file_time_type written{};
        std::uintmax_t length = 0;
    };

    std::vector<Watched> watched;
};

State state;

/* The scale the frame being built is drawn at: the display's for the window's, and 1 for a
 * capture's. See State::scale. */
float Scale()
{
    return state.capturing ? 1.0f : state.scale;
}

/* GLFW's refresh callback, called from inside PollInputEvents: the window system has uncovered
 * some of the window, or shown it, and what was there is gone. */
extern "C" void WindowRefreshed(void* window)
{
    state.stale = true;
}

/* The five below are called from inside PollInputEvents too, each after raylib's own. */
extern "C" void PointerCrossed(void* window, int entered)
{
    if (state.raylibCrossing != nullptr)
    {
        state.raylibCrossing(window, entered);
    }

    state.pointerInside = entered != 0;
}

extern "C" void ButtonChanged(void* window, int button, int action, int mods)
{
    if (state.raylibButton != nullptr)
    {
        state.raylibButton(window, button, action, mods);
    }

    /* Left, right and middle, which GLFW and ImGui number alike. */
    if (button >= 0 &&
        button < 3)
    {
        state.presses.push_back({button, action != glfwRelease});
        state.held[button] = action != glfwRelease;
    }
}

extern "C" void WheelTurned(void* window, double across, double down)
{
    if (state.raylibScroll != nullptr)
    {
        state.raylibScroll(window, across, down);
    }

    state.wheelAcross += static_cast<float>(across);
    state.wheelDown += static_cast<float>(down);
    state.wheelNotches += static_cast<float>(down);
}

extern "C" void KeyChanged(void* window, int key, int scancode, int action, int mods)
{
    if (state.raylibKey != nullptr)
    {
        state.raylibKey(window, key, scancode, action, mods);
    }

    state.characterFollows = false;
    if (action == glfwRelease)
    {
        return;
    }

    state.keyed = true;

    /* One repeat of a key waiting at a time. A loop that was held up for seconds is handed every
     * repeat the window system kept for it at once, and would go on scrolling for as long again
     * after the key was let go. */
    const bool repeated = action != 1;
    if (repeated)
    {
        for (const State::KeyPress& waiting : state.keys)
        {
            if (waiting.repeated &&
                waiting.key == key &&
                waiting.scancode == scancode)
            {
                return;
            }
        }
    }

    state.keys.push_back({key, scancode, mods, repeated, 0});
    state.characterFollows = true;
}

extern "C" void CharacterTyped(void* window, unsigned int codepoint)
{
    if (state.raylibCharacter != nullptr)
    {
        state.raylibCharacter(window, codepoint);
    }

    /* Typed by the press just queued: GLFW reports a key and then its character. One that follows
     * a repeat that was not queued goes with it, and one that comes by itself is what an input
     * method composed, which is none of the keys read here. */
    if (state.characterFollows &&
        !state.keys.empty())
    {
        state.keys.back().character = codepoint;
    }

    state.characterFollows = false;
}

/*
 * Whether a remembered window would open somewhere it can be reached: its top edge on a monitor,
 * with enough of its width there to take hold of. Monitors come and go between runs, and a window
 * opened where one used to be is a viewer that looks as if it did not start.
 */
bool OnAMonitor(const DeviewPlacement& placement)
{
    constexpr int reach = 100;
    const int count = GetMonitorCount();
    for (int index = 0; index < count; index++)
    {
        const Vector2 origin = GetMonitorPosition(index);
        const int left = static_cast<int>(origin.x);
        const int top = static_cast<int>(origin.y);
        const int right = left + GetMonitorWidth(index);
        const int bottom = top + GetMonitorHeight(index);
        const int overlap = std::min(placement.x + placement.width, right) - std::max(placement.x, left);
        if (overlap >= reach &&
            placement.y >= top &&
            placement.y < bottom - reach)
        {
            return true;
        }
    }

    return false;
}

/* Noted every frame the window is neither maximised, minimised nor hidden. See State::normal. */
void TrackPlacement()
{
    if (IsWindowState(FLAG_WINDOW_HIDDEN) ||
        IsWindowMinimized())
    {
        return;
    }

    state.normal.maximized = IsWindowMaximized() ? 1 : 0;
    if (state.normal.maximized != 0 &&
        state.tracked)
    {
        return;
    }

    const Vector2 position = GetWindowPosition();
    state.normal.x = static_cast<int32_t>(position.x);
    state.normal.y = static_cast<int32_t>(position.y);
    state.normal.width = GetScreenWidth();
    state.normal.height = GetScreenHeight();
    state.tracked = true;
}

float ClampQueueWidth(float value, float available, float cell)
{
    const float low = cell * minQueueCells;
    const float high = std::max(low, available - cell * minPaneCells * 2.0f);
    return std::min(std::max(value, low), high);
}

void ResetInput()
{
    state.input.key = DEVIEW_KEY_NONE;
    state.input.clickedButton = -1;
    state.input.clickedQueueItem = -1;
    state.input.rightClickedQueueItem = -1;
    state.input.clickedMenuItem = -1;
    /* This head draws its own menu, so a click outside it is an ordinary click the managed side
     * already reads as a dismissal. Cleared anyway so the field never carries a stale 1. */
    state.input.menuClosed = 0;
    state.input.scrollDelta = 0;
    /* -1, not 0: zero is a legitimate scroll target, so a cleared field has to mean "no target"
     * rather than "go to the top". */
    state.input.scrollTo = -1;
    state.input.closeRequested = 0;
    /* -1 for the same reason scrollTo is: 0 is the left pane, so a cleared field has to say "no
     * drag" rather than "a drag in the left pane at row 0". */
    state.input.dragSide = -1;
    state.input.dragAnchorRow = 0;
    state.input.dragAnchorColumn = 0;
    state.input.dragFocusRow = 0;
    state.input.dragFocusColumn = 0;
    state.input.zoomDelta = 0;
    /* -1 again: 0 is the left edge of a picture, so a cleared field has to say "no drag". */
    state.input.panX = -1.0f;
    state.input.panY = -1.0f;
    state.input.rightClickedPane = -1;
}

/* Every string is an offset into one UTF-8 blob. Bad offsets are a crash, not a glitch, so the
 * whole boundary is bounds checked rather than trusted. */
bool Slice(const DeviewScreen* screen, int offset, int length, const char** begin, const char** end)
{
    if (screen->strings == nullptr ||
        offset < 0 ||
        length < 0 ||
        offset > screen->stringsLength ||
        offset + length > screen->stringsLength)
    {
        return false;
    }

    *begin = reinterpret_cast<const char*>(screen->strings) + offset;
    *end = *begin + length;
    return true;
}

void Text(const DeviewScreen* screen, int offset, int length)
{
    const char* begin;
    const char* end;
    if (Slice(screen, offset, length, &begin, &end))
    {
        ImGui::TextUnformatted(begin, end);
    }
    else
    {
        ImGui::TextUnformatted("");
    }
}

std::string Copy(const DeviewScreen* screen, int offset, int length)
{
    const char* begin;
    const char* end;
    if (!Slice(screen, offset, length, &begin, &end))
    {
        return {};
    }

    return {begin, static_cast<size_t>(end - begin)};
}

ImU32 RowColour(int kind)
{
    switch (kind)
    {
        case DEVIEW_ROW_ADDED:
            return IM_COL32(126, 214, 139, 255);
        case DEVIEW_ROW_REMOVED:
            return IM_COL32(233, 129, 129, 255);
        case DEVIEW_ROW_MODIFIED:
            return IM_COL32(231, 197, 113, 255);
        /* Dimmed like the gutter, since what it says is about the file rather than from it. */
        case DEVIEW_ROW_FOLDED:
            return IM_COL32(130, 130, 130, 255);
        default:
            return IM_COL32(212, 212, 212, 255);
    }
}

ImU32 RowBackground(int kind)
{
    switch (kind)
    {
        case DEVIEW_ROW_ADDED:
            return IM_COL32(38, 74, 44, 255);
        case DEVIEW_ROW_REMOVED:
            return IM_COL32(84, 40, 40, 255);
        case DEVIEW_ROW_MODIFIED:
            return IM_COL32(74, 64, 32, 255);
        /* A shade lighter than filler, so a fold reads as a break in the file rather than as a
         * line of it or as padding. */
        case DEVIEW_ROW_FOLDED:
            return IM_COL32(34, 34, 34, 255);
        default:
            return 0;
    }
}

char RowMarker(int kind)
{
    switch (kind)
    {
        case DEVIEW_ROW_ADDED:
            return '+';
        case DEVIEW_ROW_REMOVED:
            return '-';
        case DEVIEW_ROW_MODIFIED:
            return '~';
        default:
            return ' ';
    }
}

/* ---- pictures ---- */

/*
 * The three ways a picture's texture is sampled, by the size it is drawn at.
 *
 * Smoothed between its own pixels, which is all a picture drawn at its own size or a little under
 * needs. As the pixels it has, once it is enlarged past its own size, which is what zooming that
 * far in is for: smoothed, a one pixel difference between the two sides is a blur on both. And
 * from its reduced copies, once it is drawn at under half its size. Smoothing looks at the four
 * pixels nearest each point it samples and at none of the ones between two such points, so a
 * screenshot fitted at a third of its size lost whichever of its one pixel lines fell between
 * them, and its small text came apart. The reduced copies are each half the size of the one
 * before, every pixel of them an average of the ones it stands for, so a thin line is fainter
 * there and not gone.
 */
constexpr int sampleSmoothed = 0;
constexpr int sampleAsPixels = 1;
constexpr int sampleReduced = 2;

/*
 * How a picture's texture is sampled as it is made: smoothed. Clamped at its edges rather than
 * repeating, which is raylib's default: sampled at its last column, a repeating texture takes in
 * its first, and a picture that is opaque on the left and clear on the right grew a line of its
 * left edge down its right.
 */
void PrepareTexture(CachedTexture& entry)
{
    SetTextureFilter(entry.texture, TEXTURE_FILTER_BILINEAR);
    SetTextureWrap(entry.texture, TEXTURE_WRAP_CLAMP);
    entry.sampling = sampleSmoothed;
}

/* Changed only when it has to be, since it is a texture parameter and this is asked every
 * frame. A picture whose reduced copies could not be made is smoothed instead. */
void Sample(const std::string& path, int sampling)
{
    const auto found = state.pictures.find(path);
    if (found == state.pictures.end() ||
        !found->second.loaded)
    {
        return;
    }

    CachedTexture& entry = found->second;
    if (sampling == sampleReduced &&
        entry.texture.mipmaps <= 1)
    {
        sampling = sampleSmoothed;
    }

    if (entry.sampling == sampling)
    {
        return;
    }

    SetTextureFilter(
        entry.texture,
        sampling == sampleAsPixels ? TEXTURE_FILTER_POINT :
        sampling == sampleReduced ? TEXTURE_FILTER_TRILINEAR :
        TEXTURE_FILTER_BILINEAR);
    entry.sampling = sampling;
}

/*
 * The longest side of a texture the window's GL will take, or zero where it would not say.
 *
 * A picture past it cannot be drawn. Handed to GL all the same, it came back as a texture with a
 * name and no pixels, which draws as black: a box of it where the picture should be, in place of
 * the nothing a picture this head cannot show is drawn as. It is 16384 under Mesa's software
 * rasteriser, and a screenshot of the whole of a long page is past that.
 *
 * Asked of GL by name, through GLFW, since rlgl reads this number only to log it. On the thread
 * that owns the context, once.
 */
int TextureLimit()
{
    static int limit = -1;
    if (limit < 0)
    {
        limit = 0;
        typedef void (*GetIntegers)(unsigned int name, int* values);
        const GetIntegers getIntegers = reinterpret_cast<GetIntegers>(glfwGetProcAddress("glGetIntegerv"));
        if (getIntegers != nullptr)
        {
            constexpr unsigned int maxTextureSize = 0x0D33;
            getIntegers(maxTextureSize, &limit);
        }
    }

    return limit;
}

bool FitsATexture(const Image& image, int limit)
{
    return limit <= 0 ||
           (image.width <= limit && image.height <= limit);
}

/*
 * Whether any of a decoded picture can be seen through: whether it has a pixel that is less than
 * opaque. That is what the checkerboard behind a picture is for, and behind a picture with no such
 * pixel every square of it is covered, so it is not drawn.
 *
 * Asked of the pixels rather than of the format. A screenshot or a drawn page of a document is
 * usually saved with an alpha channel that is 255 throughout, and those are most of the pictures
 * there are. One pass over them as they are decoded, which is off the window's thread for every
 * picture but a capture's.
 */
bool SeeThrough(const Image& image)
{
    if (image.data == nullptr)
    {
        return false;
    }

    size_t stride = 0;
    size_t alpha = 0;
    switch (image.format)
    {
        case PIXELFORMAT_UNCOMPRESSED_GRAYSCALE:
        case PIXELFORMAT_UNCOMPRESSED_R8G8B8:
        case PIXELFORMAT_UNCOMPRESSED_R5G6B5:
            return false;
        case PIXELFORMAT_UNCOMPRESSED_GRAY_ALPHA:
            stride = 2;
            alpha = 1;
            break;
        case PIXELFORMAT_UNCOMPRESSED_R8G8B8A8:
            stride = 4;
            alpha = 3;
            break;
        /* Not one the decoders built here hand back for a picture the viewer shows. Taken to have
         * something to see through, which costs one quad where it has not. */
        default:
            return true;
    }

    const unsigned char* pixels = static_cast<const unsigned char*>(image.data);
    const size_t count = static_cast<size_t>(image.width) * static_cast<size_t>(image.height);
    for (size_t pixel = 0; pixel < count; pixel++)
    {
        if (pixels[pixel * stride + alpha] != 255)
        {
            return true;
        }
    }

    return false;
}

/*
 * A picture read off the disk and made ready to be a texture: whether any of it can be seen
 * through, and its reduced copies, which are made here because here is off the window's thread
 * for every picture but a capture's. The file, stb_image and raylib's resampling, and nothing
 * that touches GL. A picture no texture can hold is left as it was read, since nothing will be
 * made of it.
 */
Image ReadPicture(const std::string& path, int textureLimit, bool& translucent)
{
    Image image = LoadImage(path.c_str());
    translucent = SeeThrough(image);
    if (image.data != nullptr &&
        FitsATexture(image, textureLimit))
    {
        ImageMipmaps(&image);
    }

    return image;
}

/*
 * The texture for a picture that has been read, or false for one there is none for: a file that
 * could not be read, a picture longer on a side than a texture can be, or a context that would
 * not make one. On the thread that owns the context.
 */
bool MakeTexture(const Image& image, bool translucent, CachedTexture& entry)
{
    if (image.data == nullptr ||
        !FitsATexture(image, TextureLimit()))
    {
        return false;
    }

    const Texture2D texture = LoadTextureFromImage(image);
    if (!IsTextureValid(texture))
    {
        return false;
    }

    entry.texture = texture;
    entry.loaded = true;
    entry.translucent = translucent;
    PrepareTexture(entry);
    return true;
}

void DecodeLoop(std::shared_ptr<Decoder> decoder)
{
    std::unique_lock<std::mutex> lock(decoder->mutex);
    while (true)
    {
        decoder->wake.wait(lock, [&decoder] { return decoder->stopping || !decoder->requests.empty(); });
        if (decoder->stopping)
        {
            return;
        }

        Decode decode = std::move(decoder->requests.front());
        decoder->requests.pop_front();
        lock.unlock();

        decode.image = ReadPicture(decode.path, decoder->textureLimit, decode.translucent);

        lock.lock();
        if (decoder->stopping)
        {
            UnloadImage(decode.image);
            return;
        }

        decoder->done.push_back(std::move(decode));
    }
}

void RequestDecode(const std::string& path, std::uintmax_t length, std::filesystem::file_time_type written)
{
    if (!state.decoder)
    {
        state.decoder = std::make_shared<Decoder>();
        state.decoder->textureLimit = TextureLimit();
        std::thread(DecodeLoop, state.decoder).detach();
    }

    {
        const std::lock_guard<std::mutex> lock(state.decoder->mutex);
        Decode decode;
        decode.path = path;
        decode.length = length;
        decode.written = written;
        state.decoder->requests.push_back(std::move(decode));
    }

    state.decoder->wake.notify_one();
}

/*
 * Drops a decode not started yet, for a picture no longer on screen, so stepping quickly through a
 * queue of pictures does not leave the thread decoding every one of them in turn. One already
 * started finishes, and is thrown away when it lands.
 */
void CancelDecode(const std::string& path)
{
    if (!state.decoder)
    {
        return;
    }

    const std::lock_guard<std::mutex> lock(state.decoder->mutex);
    auto& requests = state.decoder->requests;
    requests.erase(
        std::remove_if(
            requests.begin(),
            requests.end(),
            [&path](const Decode& request) { return request.path == path; }),
        requests.end());
}

void StopDecoder()
{
    if (!state.decoder)
    {
        return;
    }

    {
        const std::lock_guard<std::mutex> lock(state.decoder->mutex);
        state.decoder->stopping = true;
        state.decoder->requests.clear();
        for (auto& decode : state.decoder->done)
        {
            UnloadImage(decode.image);
        }

        state.decoder->done.clear();
    }

    state.decoder->wake.notify_one();
    state.decoder.reset();
}

/*
 * Uploads what the decoder has finished into the entries still waiting for it. At the top of a
 * frame, on the thread that owns the GL context. A decode for an entry since forgotten, or for a
 * file since rewritten, is thrown away.
 *
 * Returns whether any entry was waiting for what landed, which is a frame to build: the picture is
 * there to draw now, or is known not to be coming and its spinner goes.
 */
bool TakeDecoded()
{
    if (!state.decoder)
    {
        return false;
    }

    std::vector<Decode> done;
    {
        const std::lock_guard<std::mutex> lock(state.decoder->mutex);
        done.swap(state.decoder->done);
    }

    bool landed = false;
    for (auto& decode : done)
    {
        const auto found = state.pictures.find(decode.path);
        if (found != state.pictures.end() &&
            found->second.decoding &&
            found->second.written == decode.written &&
            found->second.length == decode.length)
        {
            CachedTexture& entry = found->second;
            entry.decoding = false;
            landed = true;
            if (MakeTexture(decode.image, decode.translucent, entry))
            {
                /* GL hands out the name of a texture that has been unloaded again, so a frame
                 * drawn with this one can be, number for number, a frame drawn with the one
                 * that had the name before it. */
                state.stale = true;
            }
        }

        UnloadImage(decode.image);
    }

    return landed;
}

void ForgetPicture(const std::string& path)
{
    const auto found = state.pictures.find(path);
    if (found == state.pictures.end())
    {
        return;
    }

    if (found->second.loaded)
    {
        UnloadTexture(found->second.texture);
    }

    if (found->second.decoding)
    {
        CancelDecode(path);
    }

    state.pictures.erase(found);
}

/*
 * A file's write time and length, which between them say whether a picture decoded from it is
 * still what the file holds. False when either cannot be read, which is a file that has gone.
 */
bool Stamp(const std::string& path, std::filesystem::file_time_type& written, std::uintmax_t& length)
{
    const std::filesystem::path file(path);
    std::error_code error;
    written = std::filesystem::last_write_time(file, error);
    if (error)
    {
        return false;
    }

    length = std::filesystem::file_size(file, error);
    return !error;
}

/*
 * Whether the file behind any picture the last frame asked for is no longer as that frame found
 * it: written again, gone, or there where it was not. Picture asks this of each picture as a frame
 * is built. A window that is being left alone builds no frames, so it is asked of all of them here
 * before the window is left alone again.
 */
bool PicturesRewritten()
{
    for (const State::Watched& watched : state.watched)
    {
        std::filesystem::file_time_type written;
        std::uintmax_t length = 0;
        const bool there = Stamp(watched.path, written, length);
        if (there != watched.there ||
            (there && (written != watched.written || length != watched.length)))
        {
            return true;
        }
    }

    return false;
}

/*
 * The decoded picture for a path, or null when there is none to draw: either this build cannot
 * read it, or it is still being decoded, which `loading` says so the pane can show that it is coming
 * rather than nothing. A capture decodes here and now, since it draws one frame and has no later one
 * for a decode to land in.
 *
 * Invalidated by the file's write time and length, which is the same freshness test the managed
 * queue poller uses: a re-run that rewrites a received image has to refresh the pane rather than
 * leave the previous one up.
 */
const CachedTexture* Picture(const std::string& path, bool& loading)
{
    loading = false;
    if (path.empty())
    {
        return nullptr;
    }

    std::filesystem::file_time_type written;
    std::uintmax_t length = 0;
    const bool there = Stamp(path, written, length);
    if (!state.capturing)
    {
        state.watched.push_back({path, there, written, length});
    }

    if (!there)
    {
        ForgetPicture(path);
        return nullptr;
    }

    const auto found = state.pictures.find(path);
    if (found != state.pictures.end())
    {
        /* A capture does not wait on a decode the window started: it cannot. */
        if (found->second.written == written &&
            found->second.length == length &&
            !(found->second.decoding && state.capturing))
        {
            found->second.used = true;
            loading = found->second.decoding;
            return found->second.loaded ? &found->second : nullptr;
        }

        ForgetPicture(path);
    }

    CachedTexture entry;
    entry.written = written;
    entry.length = length;
    entry.used = true;
    if (state.capturing)
    {
        /* What the decoder's thread and then TakeDecoded do, here and now. */
        bool translucent = false;
        const Image image = ReadPicture(path, TextureLimit(), translucent);
        MakeTexture(image, translucent, entry);
        UnloadImage(image);
    }
    else
    {
        entry.decoding = true;
        loading = true;
        RequestDecode(path, length, written);
    }

    const auto inserted = state.pictures.emplace(path, entry).first;
    return inserted->second.loaded ? &inserted->second : nullptr;
}

/*
 * Drops every picture the frame just drawn did not ask for, once the frame has been rendered and
 * the draw data naming those textures has been consumed.
 *
 * Otherwise an entry went only when its own path was asked for again and had changed or gone, so
 * every image reviewed in a session stayed decoded, on the GPU, until the session ended. A picture
 * scrolled or navigated back to is decoded again, which is one file read.
 */
void ForgetUnusedPictures()
{
    for (auto entry = state.pictures.begin(); entry != state.pictures.end();)
    {
        if (entry->second.used)
        {
            entry->second.used = false;
            ++entry;
            continue;
        }

        if (entry->second.loaded)
        {
            UnloadTexture(entry->second.texture);
        }

        if (entry->second.decoding)
        {
            CancelDecode(entry->first);
        }

        entry = state.pictures.erase(entry);
    }
}

/*
 * The checkerboard's texture, or null on a context that would not make one, where a picture is
 * drawn over the lighter of the two tones instead.
 *
 * Sampled as its two tones and nothing between them, and repeating, which is what lets one quad
 * the size of the picture stand for every square behind it.
 */
const Texture2D* Checker()
{
    if (!state.checkerMade)
    {
        state.checkerMade = true;

        /* Light where the row and the column are both even or both odd, and dark elsewhere. */
        unsigned char pixels[] = {
            64, 64, 64, 255, 48, 48, 48, 255,
            48, 48, 48, 255, 64, 64, 64, 255};
        Image image{};
        image.data = pixels;
        image.width = 2;
        image.height = 2;
        image.mipmaps = 1;
        image.format = PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
        state.checker = LoadTextureFromImage(image);
        if (IsTextureValid(state.checker))
        {
            SetTextureFilter(state.checker, TEXTURE_FILTER_POINT);
            SetTextureWrap(state.checker, TEXTURE_WRAP_REPEAT);
        }
    }

    return IsTextureValid(state.checker) ? &state.checker : nullptr;
}

void UnloadPictures()
{
    StopDecoder();
    for (auto& entry : state.pictures)
    {
        if (entry.second.loaded)
        {
            UnloadTexture(entry.second.texture);
        }
    }

    state.pictures.clear();

    if (IsTextureValid(state.checker))
    {
        UnloadTexture(state.checker);
    }

    state.checker = Texture2D{};
    state.checkerMade = false;
}

/* ---- fonts ---- */

/*
 * The machine's own fonts, for the characters the embedded one does not have.
 *
 * JetBrains Mono has Latin, Greek, Cyrillic and the symbols code is written in, and it was the
 * only font here: Chinese, Japanese, Korean, Arabic, Hebrew, Thai and emoji all drew as the
 * replacement glyph, so a snapshot holding any of them could not be reviewed in this head. A line
 * with one such character changed was marked as changed and looked the same on both sides. The
 * other two heads have their toolkits' font fallback. ImGui has none of its own, but it does draw
 * a character from the first of a font's sources to have it, so the machine's fonts are merged
 * into the window's font as further sources.
 *
 * Drawn, and not shaped: each character is the glyph its font has for it, where the grid put it.
 * Arabic is its letters unjoined and in the order they are stored, and an emoji made of several
 * is as many of them as its cells hold. That is enough to see which characters a snapshot holds,
 * which is what this is for.
 *
 * Only the fonts a character on screen has needed, and only once one has. Every font fontconfig
 * knows of can be hundreds of megabytes of files, and a screen of ASCII, which is nearly every
 * screen, costs a pass over its bytes and nothing else. Until a font lands its characters are the
 * replacement glyph they always were.
 *
 * Never for a capture: see State::font.
 */

/* As many of the machine's fonts as are ever merged in. ImGui numbers a font's sources in four
 * bits, and the embedded font is the first of them. */
constexpr size_t fontLimit = 15;

/*
 * fontconfig's FcFontSet, whose layout is part of its ABI, and the entry points this uses.
 *
 * Found in the library when a character first needs them rather than linked. Linked, a machine
 * without fontconfig could not load this library at all, and building it would need fontconfig's
 * headers. Found at run time, such a machine has no fonts to offer, which is what every machine
 * had before.
 */
struct FontSet
{
    int count;
    int capacity;
    void** fonts;
};

struct Fontconfig
{
    void* (*initLoadConfigAndFonts)() = nullptr;
    void* (*nameParse)(const unsigned char* name) = nullptr;
    int (*configSubstitute)(void* config, void* pattern, int kind) = nullptr;
    void (*defaultSubstitute)(void* pattern) = nullptr;
    FontSet* (*fontSort)(void* config, void* pattern, int trim, void** charset, int* result) = nullptr;
    int (*patternGetString)(const void* pattern, const char* object, int index, unsigned char** value) = nullptr;
    int (*patternGetInteger)(const void* pattern, const char* object, int index, int* value) = nullptr;
    int (*patternGetBool)(const void* pattern, const char* object, int index, int* value) = nullptr;
    int (*patternGetCharSet)(const void* pattern, const char* object, int index, void** value) = nullptr;
    int (*charSetHasChar)(const void* charset, unsigned int codepoint) = nullptr;
};

/*
 * Every font on the machine that says which characters it has, in the order fontconfig falls
 * back through them from a monospace font for this user's language. That is the answer every
 * other program here is given, and it is what puts the Japanese forms of the Han characters
 * ahead of the Chinese ones for a Japanese reader.
 */
struct SystemFonts
{
    bool opened = false;
    Fontconfig fontconfig;

    struct Candidate
    {
        const void* pattern;
        const void* charset;
    };

    std::vector<Candidate> candidates;

    /* Handed over already, and found to be something stb_truetype cannot draw from. By file and
     * face rather than by candidate, because one face can be listed more than once. */
    std::set<std::pair<std::string, int>> taken;
    std::set<std::pair<std::string, int>> unusable;
};

#if !defined(_WIN32)
template <typename Entry>
bool Resolve(void* library, const char* name, Entry& entry)
{
    entry = reinterpret_cast<Entry>(dlsym(library, name));
    return entry != nullptr;
}
#endif

/* On the finder's thread, the first time a character is asked about. What fontconfig hands back
 * is not given back: the candidates point into it for as long as the thread runs, which is as
 * long as the window does, and a process has the one window. */
void OpenSystemFonts(SystemFonts& fonts)
{
    fonts.opened = true;
#if !defined(_WIN32)
    void* library = dlopen("libfontconfig.so.1", RTLD_NOW | RTLD_LOCAL);
    if (library == nullptr)
    {
        return;
    }

    Fontconfig& fontconfig = fonts.fontconfig;
    if (!Resolve(library, "FcInitLoadConfigAndFonts", fontconfig.initLoadConfigAndFonts) ||
        !Resolve(library, "FcNameParse", fontconfig.nameParse) ||
        !Resolve(library, "FcConfigSubstitute", fontconfig.configSubstitute) ||
        !Resolve(library, "FcDefaultSubstitute", fontconfig.defaultSubstitute) ||
        !Resolve(library, "FcFontSort", fontconfig.fontSort) ||
        !Resolve(library, "FcPatternGetString", fontconfig.patternGetString) ||
        !Resolve(library, "FcPatternGetInteger", fontconfig.patternGetInteger) ||
        !Resolve(library, "FcPatternGetBool", fontconfig.patternGetBool) ||
        !Resolve(library, "FcPatternGetCharSet", fontconfig.patternGetCharSet) ||
        !Resolve(library, "FcCharSetHasChar", fontconfig.charSetHasChar))
    {
        return;
    }

    void* config = fontconfig.initLoadConfigAndFonts();
    void* pattern = fontconfig.nameParse(reinterpret_cast<const unsigned char*>("monospace"));
    if (config == nullptr ||
        pattern == nullptr)
    {
        return;
    }

    /* The two steps every match is prepared with: the configuration's rules, which is where
     * "monospace" becomes the fonts the machine means by it, and the defaults, which is where
     * the user's language comes from. */
    fontconfig.configSubstitute(config, pattern, 0);
    fontconfig.defaultSubstitute(pattern);

    /* Untrimmed. Trimming leaves out a font with no character the ones ahead of it lack, which
     * it works out by uniting every character set in turn, and each character is asked of them
     * here anyway. */
    int result = 0;
    const FontSet* sorted = fontconfig.fontSort(config, pattern, 0, nullptr, &result);
    if (sorted == nullptr)
    {
        return;
    }

    for (int index = 0; index < sorted->count; index++)
    {
        void* charset = nullptr;
        if (fontconfig.patternGetCharSet(sorted->fonts[index], "charset", 0, &charset) == 0 &&
            charset != nullptr)
        {
            fonts.candidates.push_back({sorted->fonts[index], charset});
        }
    }
#endif
}

constexpr uint32_t Tag(char first, char second, char third, char fourth)
{
    return static_cast<uint32_t>(static_cast<unsigned char>(first)) << 24 |
           static_cast<uint32_t>(static_cast<unsigned char>(second)) << 16 |
           static_cast<uint32_t>(static_cast<unsigned char>(third)) << 8 |
           static_cast<uint32_t>(static_cast<unsigned char>(fourth));
}

/*
 * The scale that makes deview_init's size an em for a face: its ascent plus its descent over its
 * em, which is what emScale is for the embedded font. Merged at ImGui's own scale it is each
 * font's height that is matched, so one with tall lines comes out small and one with short lines
 * large. An em is what the other two heads fall back at, and what leaves a CJK character, an em
 * wide, inside the two cells the grid gives it.
 *
 * False for a face stb_truetype, which is what rasterises here, cannot draw from. Those are its
 * own conditions, asked first. It wants outlines, as TrueType's or in a CFF table, and a variable
 * font of the CFF2 kind has neither, which is one of the forms Noto CJK comes in. Asked here, on
 * the finder's thread, the next font with the character is tried instead; left to ImGui, the
 * refusal comes on the render thread as an error, and nothing else is tried.
 */
bool EmScaleOf(const std::vector<unsigned char>& data, int face, float& scale)
{
    const size_t size = data.size();
    const auto u16 = [&data, size](size_t at) -> uint32_t
    {
        return at + 2 <= size
            ? static_cast<uint32_t>(data[at]) << 8 | static_cast<uint32_t>(data[at + 1])
            : 0;
    };
    const auto u32 = [&u16](size_t at) -> uint32_t { return u16(at) << 16 | u16(at + 2); };

    /* A collection starts with where each of its faces does. */
    size_t start = 0;
    if (u32(0) == Tag('t', 't', 'c', 'f'))
    {
        if (static_cast<uint32_t>(face) >= u32(8))
        {
            return false;
        }

        start = u32(12 + static_cast<size_t>(face) * 4);
    }
    else if (face != 0)
    {
        return false;
    }

    size_t head = 0;
    size_t hhea = 0;
    bool cmap = false;
    bool hmtx = false;
    bool glyf = false;
    bool loca = false;
    bool cff = false;
    const uint32_t tables = u16(start + 4);
    for (uint32_t table = 0; table < tables; table++)
    {
        const size_t record = start + 12 + static_cast<size_t>(table) * 16;
        switch (u32(record))
        {
            case Tag('h', 'e', 'a', 'd'): head = u32(record + 8); break;
            case Tag('h', 'h', 'e', 'a'): hhea = u32(record + 8); break;
            case Tag('c', 'm', 'a', 'p'): cmap = true; break;
            case Tag('h', 'm', 't', 'x'): hmtx = true; break;
            case Tag('g', 'l', 'y', 'f'): glyf = true; break;
            case Tag('l', 'o', 'c', 'a'): loca = true; break;
            case Tag('C', 'F', 'F', ' '): cff = true; break;
            default: break;
        }
    }

    const int unitsPerEm = static_cast<int>(u16(head + 18));
    const int ascent = static_cast<int16_t>(u16(hhea + 4));
    const int descent = static_cast<int16_t>(u16(hhea + 6));
    if (head == 0 ||
        hhea == 0 ||
        !cmap ||
        !hmtx ||
        !(cff || (glyf && loca)) ||
        unitsPerEm == 0 ||
        ascent <= descent)
    {
        return false;
    }

    scale = static_cast<float>(ascent - descent) / static_cast<float>(unitsPerEm);
    return true;
}

bool ReadFont(const Fontconfig& fontconfig, const void* pattern, const std::pair<std::string, int>& face, FoundFont& found)
{
    /* Outlines, and nothing else. A bitmap font has none to scale, and a colour font - which is
     * what the emoji font usually is - keeps its pictures in tables stb_truetype does not read.
     * Merged, it would draw every emoji as nothing, ahead of a font with plain ones. */
    int flag = 0;
    if ((fontconfig.patternGetBool(pattern, "outline", 0, &flag) == 0 && flag == 0) ||
        (fontconfig.patternGetBool(pattern, "color", 0, &flag) == 0 && flag != 0))
    {
        return false;
    }

    std::ifstream file(face.first, std::ios::binary | std::ios::ate);
    const std::streamoff length = file.tellg();
    /* ImGui takes a length as an int. */
    if (!file ||
        length <= 0 ||
        length > INT_MAX)
    {
        return false;
    }

    std::vector<unsigned char> data(static_cast<size_t>(length));
    file.seekg(0);
    if (!file.read(reinterpret_cast<char*>(data.data()), length) ||
        !EmScaleOf(data, face.second, found.scale))
    {
        return false;
    }

    found.data = std::move(data);
    found.face = face.second;
    return true;
}

/*
 * The first font in fontconfig's order to have a character and be one that can be drawn from,
 * read whole. False when there is none, and when that font has been handed over already, since
 * the character is then on its way with it.
 */
bool FindFont(SystemFonts& fonts, uint32_t codepoint, FoundFont& found)
{
    if (!fonts.opened)
    {
        OpenSystemFonts(fonts);
    }

    const Fontconfig& fontconfig = fonts.fontconfig;
    for (const SystemFonts::Candidate& candidate : fonts.candidates)
    {
        if (fontconfig.charSetHasChar(candidate.charset, codepoint) == 0)
        {
            continue;
        }

        unsigned char* file = nullptr;
        if (fontconfig.patternGetString(candidate.pattern, "file", 0, &file) != 0 ||
            file == nullptr)
        {
            continue;
        }

        /* The face of a collection is the low half of the index. The high half names an instance
         * of a variable font, which stb_truetype draws as its default whichever is asked for. */
        int index = 0;
        fontconfig.patternGetInteger(candidate.pattern, "index", 0, &index);
        const std::pair<std::string, int> face(reinterpret_cast<const char*>(file), index & 0xFFFF);
        if (fonts.taken.count(face) != 0)
        {
            return false;
        }

        if (fonts.unusable.count(face) != 0)
        {
            continue;
        }

        if (ReadFont(fontconfig, candidate.pattern, face, found))
        {
            fonts.taken.insert(face);
            return true;
        }

        fonts.unusable.insert(face);
    }

    return false;
}

void FindFonts(std::shared_ptr<FontFinder> finder)
{
    SystemFonts fonts;
    std::unique_lock<std::mutex> lock(finder->mutex);
    while (true)
    {
        finder->wake.wait(lock, [&finder] { return finder->stopping || !finder->wanted.empty(); });
        if (finder->stopping)
        {
            return;
        }

        std::vector<uint32_t> wanted;
        wanted.swap(finder->wanted);
        lock.unlock();

        /* fontconfig and the disk, and nothing of ImGui's, which belongs to the other thread. */
        std::vector<FoundFont> found;
        for (const uint32_t codepoint : wanted)
        {
            FoundFont font;
            if (FindFont(fonts, codepoint, font))
            {
                found.push_back(std::move(font));
            }
        }

        lock.lock();
        for (FoundFont& font : found)
        {
            finder->found.push_back(std::move(font));
        }
    }
}

void StopFontFinder()
{
    if (!state.finder)
    {
        return;
    }

    {
        const std::lock_guard<std::mutex> lock(state.finder->mutex);
        state.finder->stopping = true;
        state.finder->wanted.clear();
        state.finder->found.clear();
    }

    state.finder->wake.notify_one();
    state.finder.reset();
}

/*
 * Asks for a font for every character of a screen that the window's font cannot draw. Every
 * string of the frame is in the one blob, so one pass over it covers the title, the queue and
 * the tooltips with the rows.
 */
void FindFontsFor(const DeviewScreen* screen)
{
    if (state.font == nullptr ||
        screen->strings == nullptr ||
        state.fontData.size() >= fontLimit)
    {
        return;
    }

    const char* text = reinterpret_cast<const char*>(screen->strings);
    const char* const end = text + screen->stringsLength;
    std::vector<uint32_t> wanted;
    while (text < end)
    {
        /* ASCII, all of which the embedded font has, and which is nearly every byte of nearly
         * every screen. */
        if (static_cast<unsigned char>(*text) < 0x80)
        {
            text++;
            continue;
        }

        unsigned int codepoint = 0;
        text += std::max(1, ImTextCharFromUtf8(&codepoint, text, end));
        if (codepoint > IM_UNICODE_CODEPOINT_MAX)
        {
            continue;
        }

        if (state.asked.empty())
        {
            state.asked.resize(static_cast<size_t>(IM_UNICODE_CODEPOINT_MAX) + 1);
        }

        if (state.asked[codepoint])
        {
            continue;
        }

        state.asked[codepoint] = true;
        if (!state.font->IsGlyphInFont(static_cast<ImWchar>(codepoint)))
        {
            wanted.push_back(codepoint);
        }
    }

    if (wanted.empty())
    {
        return;
    }

    if (!state.finder)
    {
        state.finder = std::make_shared<FontFinder>();
        std::thread(FindFonts, state.finder).detach();
    }

    {
        const std::lock_guard<std::mutex> lock(state.finder->mutex);
        state.finder->wanted.insert(state.finder->wanted.end(), wanted.begin(), wanted.end());
    }

    state.finder->wake.notify_one();
}

/*
 * Merges what the finder has read into the window's font. At the top of a frame: ImGui takes a
 * new source between frames, and drops what it had rasterised from the font as it does, so a
 * character already drawn as missing is looked for again.
 *
 * Returns whether a font was merged, which is a frame to build: characters on the screen as the
 * replacement glyph may have a glyph now.
 */
bool TakeFonts()
{
    if (!state.finder)
    {
        return false;
    }

    std::vector<FoundFont> found;
    {
        const std::lock_guard<std::mutex> lock(state.finder->mutex);
        found.swap(state.finder->found);
    }

    bool merged = false;
    ImGuiIO& io = ImGui::GetIO();
    for (FoundFont& font : found)
    {
        if (state.fontData.size() >= fontLimit)
        {
            return merged;
        }

        state.fontData.push_back(std::move(font.data));
        std::vector<unsigned char>& data = state.fontData.back();

        /* Merged into the font added before it, which is the window's: see deview_init. */
        ImFontConfig config;
        config.MergeMode = true;
        config.FontNo = static_cast<ImU32>(font.face);
        config.ExtraSizeScale = font.scale;
        config.FontDataOwnedByAtlas = false;

        /* A font stb_truetype turns out not to read after all is left out. That is nothing for
         * ImGui to assert or to write to its log, which is what it does with a font it is given
         * and cannot use. */
        const bool asserts = io.ConfigErrorRecoveryEnableAssert;
        const bool logs = io.ConfigErrorRecoveryEnableDebugLog;
        io.ConfigErrorRecoveryEnableAssert = false;
        io.ConfigErrorRecoveryEnableDebugLog = false;
        const ImFont* added = io.Fonts->AddFontFromMemoryTTF(
            data.data(),
            static_cast<int>(data.size()),
            0.0f,
            &config);
        io.ConfigErrorRecoveryEnableAssert = asserts;
        io.ConfigErrorRecoveryEnableDebugLog = logs;
        if (added == nullptr)
        {
            state.fontData.pop_back();
            continue;
        }

        merged = true;
    }

    return merged;
}

ImFont* AddEmbeddedFont(const uint8_t* fontTtf, int32_t fontLength, float fontSize)
{
    /* ImGui frees font data with its own allocator, so hand it a copy rather than memory owned
     * by the managed heap. */
    void* copy = IM_ALLOC(static_cast<size_t>(fontLength));
    memcpy(copy, fontTtf, static_cast<size_t>(fontLength));
    ImFontConfig config;
    config.FontDataOwnedByAtlas = true;
    config.ExtraSizeScale = emScale;
    return ImGui::GetIO().Fonts->AddFontFromMemoryTTF(copy, fontLength, fontSize <= 0.0f ? 15.0f : fontSize, &config);
}

/* ---- texture protocol (ImGuiBackendFlags_RendererHasTextures) ---- */

void UpdateTexture(ImTextureData* texture)
{
    if (texture->Status == ImTextureStatus_WantCreate)
    {
        const int format = texture->Format == ImTextureFormat_Alpha8
            ? RL_PIXELFORMAT_UNCOMPRESSED_GRAYSCALE
            : RL_PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
        const unsigned int id = rlLoadTexture(texture->GetPixels(), texture->Width, texture->Height, format, 1);
        texture->SetTexID(static_cast<ImTextureID>(id));
        texture->SetStatus(ImTextureStatus_OK);
        return;
    }

    if (texture->Status == ImTextureStatus_WantUpdates)
    {
        /* Re-uploading the whole texture keeps the source rows contiguous, which a sub rectangle
         * of a wider buffer is not. Atlas updates only happen when new glyphs appear, so the extra
         * bandwidth is irrelevant next to the copy that avoiding it would need. */
        const int format = texture->Format == ImTextureFormat_Alpha8
            ? RL_PIXELFORMAT_UNCOMPRESSED_GRAYSCALE
            : RL_PIXELFORMAT_UNCOMPRESSED_R8G8B8A8;
        rlUpdateTexture(
            static_cast<unsigned int>(texture->TexID),
            0,
            0,
            texture->Width,
            texture->Height,
            format,
            texture->GetPixels());
        texture->SetStatus(ImTextureStatus_OK);
        return;
    }

    if (texture->Status == ImTextureStatus_WantDestroy)
    {
        rlUnloadTexture(static_cast<unsigned int>(texture->TexID));
        texture->SetTexID(ImTextureID_Invalid);
        texture->SetStatus(ImTextureStatus_Destroyed);
    }
}

/* ---- ImDrawData through rlgl ---- */

void RenderTriangles(
    unsigned int count,
    unsigned int indexStart,
    unsigned int vertexOffset,
    const ImVector<ImDrawIdx>& indices,
    const ImVector<ImDrawVert>& vertices,
    ImTextureID textureId)
{
    if (count < 3)
    {
        return;
    }

    rlBegin(RL_TRIANGLES);
    rlSetTexture(static_cast<unsigned int>(textureId));

    for (unsigned int index = 0; index <= count - 3; index += 3)
    {
        for (unsigned int corner = 0; corner < 3; corner++)
        {
            /* Plus the command's own vertex offset. ImDrawIdx is sixteen bits, so a draw list
             * that runs past 65535 vertices - a maximised 4K window of dense long lines gets
             * there - is split by ImGui into commands whose indices restart from a base recorded
             * here. Without adding it the indices wrapped and the panes drew scrambled, and in a
             * release build, with IM_ASSERT compiled out, nothing said so. */
            const ImDrawVert& vertex = vertices[vertexOffset + indices[indexStart + index + corner]];
            const ImColor colour = ImColor(vertex.col);
            rlColor4f(colour.Value.x, colour.Value.y, colour.Value.z, colour.Value.w);
            rlTexCoord2f(vertex.uv.x, vertex.uv.y);
            rlVertex2f(vertex.pos.x, vertex.pos.y);
        }
    }

    rlEnd();
}

void RenderDrawData(ImDrawData* drawData)
{
    for (ImTextureData* texture : drawData->Textures ? *drawData->Textures : ImVector<ImTextureData*>())
    {
        if (texture->Status != ImTextureStatus_OK)
        {
            UpdateTexture(texture);
        }
    }

    rlDrawRenderBatchActive();
    rlDisableBackfaceCulling();

    /* The height of what is being drawn to, which is not the window's for a capture: that draws to
     * a render texture of its own size, and BeginTextureMode changes the target without changing
     * what GetScreenHeight reports. */
    const float height = drawData->DisplaySize.y;
    for (int list = 0; list < drawData->CmdListsCount; list++)
    {
        const ImDrawList* commands = drawData->CmdLists[list];
        for (const ImDrawCmd& command : commands->CmdBuffer)
        {
            if (command.UserCallback != nullptr)
            {
                command.UserCallback(commands, &command);
                continue;
            }

            /* ImGui clips in framebuffer space with the origin top left; rlgl scissors from the
             * bottom left. */
            rlEnableScissorTest();
            rlScissor(
                static_cast<int>(command.ClipRect.x),
                static_cast<int>(height - command.ClipRect.w),
                static_cast<int>(command.ClipRect.z - command.ClipRect.x),
                static_cast<int>(command.ClipRect.w - command.ClipRect.y));

            RenderTriangles(
                command.ElemCount,
                command.IdxOffset,
                command.VtxOffset,
                commands->IdxBuffer,
                commands->VtxBuffer,
                command.GetTexID());

            rlDrawRenderBatchActive();
        }
    }

    rlSetTexture(0);
    rlDisableScissorTest();
    rlEnableBackfaceCulling();
}

/* ---- input ---- */

/*
 * `elapsed` is the time since the present before this one began, built or not, which is a frame's
 * length while frames are coming. Not the time since the last frame ImGui was given: after a
 * window has been left alone for an hour, that would have a tooltip's delay, and everything else
 * ImGui times, over in the first frame.
 */
void PumpInput(float elapsed)
{
    ImGuiIO& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(static_cast<float>(GetScreenWidth()), static_cast<float>(GetScreenHeight()));
    io.DeltaTime = elapsed;

    /* Nowhere, for a pointer that has left the window: see State::pointerInside. */
    const Vector2 mouse = GetMousePosition();
    if (state.pointerGone)
    {
        io.AddMousePosEvent(-FLT_MAX, -FLT_MAX);
    }
    else
    {
        io.AddMousePosEvent(mouse.x, mouse.y);
    }

    /* Every press and release since the last frame built, in the order they came. */
    for (const State::Press& press : state.presses)
    {
        io.AddMouseButtonEvent(press.button, press.down);
    }

    state.presses.clear();

    if (state.wheelAcross != 0.0f ||
        state.wheelDown != 0.0f)
    {
        io.AddMouseWheelEvent(state.wheelAcross, state.wheelDown);
        state.wheelAcross = 0.0f;
        state.wheelDown = 0.0f;
    }
}

/* The one character a string is, or zero for a string that is none or several. */
unsigned int OnlyCharacter(const char* text)
{
    if (text == nullptr ||
        *text == '\0')
    {
        return 0;
    }

    unsigned int codepoint = 0;
    const int length = ImTextCharFromUtf8(&codepoint, text, nullptr);
    return length > 0 && text[length] == '\0' ? codepoint : 0;
}

/* Whether the layout in use has a key that types a letter, unshifted. Asked of every key GLFW
 * names, which are the ones that type something, by their own numbers. */
bool LayoutTypes(unsigned int letter)
{
    for (int key = KEY_APOSTROPHE; key <= 162; key++)
    {
        if (OnlyCharacter(glfwGetKeyName(key, 0)) == letter)
        {
            return true;
        }
    }

    return false;
}

/* Whether the layout in use has a key that types any of the letters a to z, unshifted. */
bool LayoutIsLatin()
{
    for (int key = KEY_APOSTROPHE; key <= 162; key++)
    {
        const unsigned int typed = OnlyCharacter(glfwGetKeyName(key, 0));
        if (typed >= 'a' &&
            typed <= 'z')
        {
            return true;
        }
    }

    return false;
}

/*
 * Which character a key press is to be read as, in lower case, or zero for none.
 *
 * What it typed, where it typed something. While control is held nothing is typed, and it is what
 * the key types unshifted on the layout in use, which GLFW will say: read by position, as a chord
 * was, Ctrl+A on AZERTY was the key labelled Q, and Ctrl+C on Dvorak the one labelled J.
 *
 * A layout with no Latin letters has neither. Russian, Greek, Hebrew and Arabic type their own
 * letters from the keys a US keyboard has A to Z on, so not one of this head's letters could be
 * typed there, and its user had the footer's buttons and nothing else. Then the key is read as
 * the letter a US keyboard has in its place, which is what such a keyboard has printed on it
 * beside its own. Only where no key of the layout
 * types that letter: a layout that has it somewhere else keeps it there and nowhere else, or a
 * Turkish keyboard's dotless i, which sits where a US keyboard has R, would toggle the drawing.
 */
unsigned int LetterOf(const State::KeyPress& press)
{
    const unsigned int typed = press.character != 0
        ? press.character
        : OnlyCharacter(glfwGetKeyName(press.key, press.scancode));
    if (typed == 0)
    {
        return 0;
    }

    if (typed < 0x80)
    {
        /* Which letter, and nothing of its case. A capital says that Shift or Caps Lock was on and
         * not which of them, so read as typed Caps Lock turned a plain A into accept all - every
         * pending snapshot written into source, with nothing asked first, by the key that accepts
         * one - and left D, V, Q, N, P, M, R and J doing nothing. */
        return typed >= 'A' && typed <= 'Z' ? typed + ('a' - 'A') : typed;
    }

    if (press.key >= KEY_A &&
        press.key <= KEY_Z)
    {
        const unsigned int letter = static_cast<unsigned int>(press.key - KEY_A) + 'a';
        if (!LayoutTypes(letter))
        {
            return letter;
        }
    }

    /*
     * And the five keys that are not letters, on a layout with no Latin letters at all: Russian
     * and Arabic type letters of their own where a US keyboard has [ and ], so a paged document
     * had no key for its pages there, and Persian types its own digits, so zoom had no key to
     * put it back. Only on such a layout. A German keyboard types u with a diaeresis from the
     * key where [ is and has [ somewhere else, and reading it by position would give a letter
     * of its user's own language a command.
     */
    if (!LayoutIsLatin())
    {
        switch (press.key)
        {
            case KEY_LEFT_BRACKET: return '[';
            case KEY_RIGHT_BRACKET: return ']';
            case KEY_ZERO: return '0';
            case KEY_MINUS: return '-';
            case KEY_EQUAL: return '=';
            default: break;
        }
    }

    return 0;
}

/*
 * Whether a command goes on for as long as its key is held, at the rate the window system repeats
 * a key: the ones that move through what is being read, and no other.
 *
 * A letter repeated like an arrow. A held a accepted the entry on screen and then every one that
 * took its place, into source, none of them read, and a held d discarded them. The WinForms head
 * gives what changes the queue a press each for that reason. Here the rest of what is not
 * navigation acts once as well: a held m, r or j is the view flipping thirty times a second, and
 * there is nothing more for a held q or 0 to do.
 */
bool Repeats(int key)
{
    switch (key)
    {
        case DEVIEW_KEY_SCROLL_UP:
        case DEVIEW_KEY_SCROLL_DOWN:
        case DEVIEW_KEY_PAGE_UP:
        case DEVIEW_KEY_PAGE_DOWN:
        case DEVIEW_KEY_NEXT_CHANGE:
        case DEVIEW_KEY_PREVIOUS_CHANGE:
        case DEVIEW_KEY_NEXT_VARIANT:
        case DEVIEW_KEY_PREVIOUS_PAGE:
        case DEVIEW_KEY_NEXT_PAGE:
        case DEVIEW_KEY_ZOOM_IN:
        case DEVIEW_KEY_ZOOM_OUT:
            return true;
        default:
            return false;
    }
}

/* What a key going down asks for, or none for a key this head has no use for, whether it is the
 * press or one of its repeats: see KeyOf. */
int Pressed(const State::KeyPress& press)
{
    const unsigned int letter = LetterOf(press);

    /* Super as well as control, so a macOS keyboard driving the Linux build through a remote
     * session still copies with the chord its user has in their fingers. */
    if ((press.mods & (glfwControl | glfwSuper)) != 0)
    {
        if (press.repeated)
        {
            return DEVIEW_KEY_NONE;
        }

        /* Answered before the unmodified keys below, and returning none for anything else: without
         * this ctrl+a fell through to plain A, which accepts. */
        switch (letter)
        {
            case 'c': return DEVIEW_KEY_COPY;
            case 'a': return DEVIEW_KEY_SELECT_ALL;
            /* With control as well as without, since that is the chord everything else that zooms
             * taught. */
            case '+':
            case '=': return DEVIEW_KEY_ZOOM_IN;
            case '-': return DEVIEW_KEY_ZOOM_OUT;
            case '0': return DEVIEW_KEY_ZOOM_RESET;
            default: break;
        }

        /* And by position, as these three always were, for a layout whose key there types
         * something else unshifted: AZERTY has its digits on Shift. The keypad's are the same
         * keys on every layout. */
        switch (press.key)
        {
            case KEY_EQUAL:
            case KEY_KP_ADD: return DEVIEW_KEY_ZOOM_IN;
            case KEY_MINUS:
            case KEY_KP_SUBTRACT: return DEVIEW_KEY_ZOOM_OUT;
            case KEY_ZERO:
            case KEY_KP_0: return DEVIEW_KEY_ZOOM_RESET;
            default: return DEVIEW_KEY_NONE;
        }
    }

    /* The key itself held down, rather than read off the case of what was typed: see LetterOf. */
    const bool shift = (press.mods & glfwShift) != 0;

    /* Letters by the character typed rather than by key position. raylib's key codes are
     * positions on a US layout, so on AZERTY the key labelled Q reported KEY_A and accepted - a
     * snapshot written into source by a key meant to quit - while the one labelled A quit.
     * Characters follow the layout, the way the macOS and Windows heads already do. Only a key
     * that typed something: with Alt held none does, and Alt+A is not this head's to act on. */
    if (press.character != 0)
    {
        switch (letter)
        {
            /* Accept all is A with Shift held, which is what the other two heads go by. */
            case 'a': return shift ? DEVIEW_KEY_ACCEPT_ALL : DEVIEW_KEY_ACCEPT;
            case 'd': return DEVIEW_KEY_DISCARD;
            case 'v': return DEVIEW_KEY_NEXT_VARIANT;
            case 'q': return DEVIEW_KEY_QUIT;
            case 'n': return DEVIEW_KEY_NEXT_CHANGE;
            case 'p': return DEVIEW_KEY_PREVIOUS_CHANGE;
            case 'm': return DEVIEW_KEY_TOGGLE_MINIMAL;
            case 'r': return DEVIEW_KEY_TOGGLE_DRAWING;
            case 'j': return DEVIEW_KEY_NEXT_PROJECTION;
            case '[': return DEVIEW_KEY_PREVIOUS_PAGE;
            case ']': return DEVIEW_KEY_NEXT_PAGE;
            /* Plus is the equals key whether or not shift is held: nobody reaches for shift to
             * zoom in. */
            case '+':
            case '=': return DEVIEW_KEY_ZOOM_IN;
            case '-': return DEVIEW_KEY_ZOOM_OUT;
            case '0': return DEVIEW_KEY_ZOOM_RESET;
            default: break;
        }
    }

    switch (press.key)
    {
        case KEY_UP: return DEVIEW_KEY_SCROLL_UP;
        case KEY_DOWN: return DEVIEW_KEY_SCROLL_DOWN;
        case KEY_PAGE_UP: return DEVIEW_KEY_PAGE_UP;
        case KEY_PAGE_DOWN: return DEVIEW_KEY_PAGE_DOWN;
        case KEY_HOME: return DEVIEW_KEY_HOME;
        case KEY_END: return DEVIEW_KEY_END;
        case KEY_TAB: return shift ? DEVIEW_KEY_PREVIOUS_ITEM : DEVIEW_KEY_NEXT_ITEM;
        case KEY_ESCAPE: return DEVIEW_KEY_QUIT;
        default: return DEVIEW_KEY_NONE;
    }
}

/* What one key press asks for, or none: a key this head has no use for, or a repeat of one that
 * acts once however long it is held. */
int KeyOf(const State::KeyPress& press)
{
    const int key = Pressed(press);
    return press.repeated && !Repeats(key) ? DEVIEW_KEY_NONE : key;
}

/*
 * The next key waiting that asks for anything, and whether it was Escape. One a poll: the rest
 * wait for the polls after it, which is what keeps two keys pressed between two polls both acted
 * on, and a key pressed and let go between two of raylib's readings acted on at all.
 */
int ReadKey(bool& escape)
{
    escape = false;
    while (!state.keys.empty())
    {
        const State::KeyPress press = state.keys.front();
        state.keys.pop_front();
        const int key = KeyOf(press);
        if (key != DEVIEW_KEY_NONE)
        {
            escape = press.key == KEY_ESCAPE;
            return key;
        }
    }

    return DEVIEW_KEY_NONE;
}

/*
 * The window size in character cells. Measured from the font that was actually loaded, because
 * this side is the only one that knows it: the managed side used to divide pixels by a hardcoded
 * 9 by 18, which is why the viewer had no DPI handling at all.
 *
 * A row is one text line plus the spacing between rows, which is what the table the panes are
 * drawn in lays out on.
 */
void MeasureGrid()
{
    /* As the last frame built for the window found them, once there has been one. Between frames
     * ImGui answers with the font at the size it was added at, which on a scaled display is not
     * the size a frame draws it at: asked here, a window at twice the scale was told it had
     * twice the rows it has. */
    ImGui::SetCurrentContext(state.context);
    const float width = state.cellWidth > 0.0f ? state.cellWidth : ImGui::CalcTextSize("M").x;
    const float height = state.lineHeight > 0.0f ? state.lineHeight : ImGui::GetTextLineHeightWithSpacing();
    state.input.columns = width > 0.0f
        ? static_cast<int32_t>(static_cast<float>(GetScreenWidth()) / width)
        : 0;
    state.input.rows = height > 0.0f
        ? static_cast<int32_t>(static_cast<float>(GetScreenHeight()) / height)
        : 0;
}

/* ---- the frame ---- */

/*
 * Where a pane's rows landed, gathered while they are drawn rather than recomputed afterwards.
 * The table owns the pane split and the gutter is a formatted string rather than a fixed number of
 * cells, so asking the layout is the only way a drag can be resolved against the same numbers that
 * drew the text it is selecting.
 */
struct PaneHit
{
    /* The left edge of the column, which is where the gutter starts. */
    float cellLeft = -1.0f;

    /* Where the row text starts, past that gutter, read from the first row that draws any, and
     * true of every row because GutterDigits gives them all one width. Stays -1 for a pane of
     * nothing but filler, which has nothing to select either. */
    float textLeft = -1.0f;

    /* The top of row zero and the pitch between rows, read from the first two rows the way
     * PaneImage reads them. */
    float first = -1.0f;
    float pitch = 0.0f;
};

/*
 * How many digits the line numbers of this frame take: four, the width every other renderer
 * gives them, or more when a row drawn in either pane needs it.
 *
 * One width for every row of both panes, which is what lets the text start read from the first
 * row stand for all of them. Formatted per row, a five digit number pushed its own row's text a
 * cell right of the rows above it, and a drag across them selected a cell off.
 */
int GutterDigits(const DeviewScreen* screen)
{
    int digits = 4;
    for (int side = 0; side < 2 && side < screen->paneCount; side++)
    {
        const DeviewPane& pane = screen->panes[side];
        for (int index = 0; index < pane.rowCount; index++)
        {
            int32_t number = screen->rows[pane.rowOffset + index].lineNumber;
            int length = 1;
            while (number >= 10)
            {
                number /= 10;
                length++;
            }

            digits = std::max(digits, length);
        }
    }

    return digits;
}

/*
 * A row's text, each segment at its cell column: see DeviewSegment. A row that is one segment is
 * drawn as the whole row always was, through the text item that also lays the row out; that is a
 * row of plain text, which is nearly all of them. Any other row puts its segments on the window's
 * draw list at their columns, clipped to the table cell like the item would be, and keeps its
 * place in the layout with an item as tall as a line.
 */
void RowText(const DeviewScreen* screen, const DeviewRow& row, ImVec2 textPos)
{
    if (row.segmentCount <= 1 ||
        screen->segments == nullptr ||
        row.segmentOffset < 0 ||
        row.segmentOffset + row.segmentCount > screen->segmentCount)
    {
        Text(screen, row.textOffset, row.textLength);
        return;
    }

    const float cell = ImGui::CalcTextSize("M").x;
    ImDrawList* list = ImGui::GetWindowDrawList();
    const ImU32 colour = ImGui::GetColorU32(ImGuiCol_Text);
    for (int index = 0; index < row.segmentCount; index++)
    {
        const DeviewSegment& segment = screen->segments[row.segmentOffset + index];
        const char* begin;
        const char* end;
        if (!Slice(screen, segment.textOffset, segment.textLength, &begin, &end))
        {
            continue;
        }

        const ImVec2 position(textPos.x + static_cast<float>(segment.column) * cell, textPos.y);

        /*
         * Cut off where the next character starts, when it is drawn wider than the cells the grid
         * gave it: a character from one of the machine's fonts, which is as wide as that font
         * made it. The grid says where everything after it goes whatever its width, so drawn
         * whole it would run on under the characters that follow. Over spaces it may, there being
         * nothing there to run under, which is what leaves a warning sign or a star with a space
         * after it whole. A run from the embedded font is exactly its cells and is never cut, and
         * the last segment has nothing after it.
         */
        if (index + 1 < row.segmentCount)
        {
            const DeviewSegment& next = screen->segments[row.segmentOffset + index + 1];
            int column = next.column;
            const char* following;
            const char* followingEnd;
            if (Slice(screen, next.textOffset, next.textLength, &following, &followingEnd))
            {
                for (; following < followingEnd && *following == ' '; following++)
                {
                    column++;
                }
            }

            const float limit = textPos.x + static_cast<float>(column) * cell;
            if (ImGui::CalcTextSize(begin, end).x > limit - position.x)
            {
                const ImVec4 cells(position.x, -FLT_MAX, limit, FLT_MAX);
                list->AddText(nullptr, 0.0f, position, colour, begin, end, 0.0f, &cells);
                continue;
            }
        }

        list->AddText(position, colour, begin, end);
    }

    ImGui::Dummy(ImVec2(0.0f, ImGui::GetTextLineHeight()));
}

void DrawRow(const DeviewScreen* screen, const DeviewPane& pane, int index, int column, int digits, PaneHit& hit)
{
    /* Before the row count check, so a pane shorter than the body still reports where its rows
     * begin and how far apart they are. */
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    if (index == 0)
    {
        hit.cellLeft = origin.x;
        hit.first = origin.y;
    }
    else if (index == 1 && hit.first >= 0.0f)
    {
        hit.pitch = origin.y - hit.first;
    }

    if (index >= pane.rowCount)
    {
        return;
    }

    const DeviewRow& row = screen->rows[pane.rowOffset + index];
    if (row.kind == DEVIEW_ROW_FILLER)
    {
        ImGui::TableSetBgColor(ImGuiTableBgTarget_CellBg, IM_COL32(28, 28, 28, 255), column);
        return;
    }

    const ImU32 background = RowBackground(row.kind);
    if (background != 0)
    {
        ImGui::TableSetBgColor(ImGuiTableBgTarget_CellBg, background, column);
    }

    ImGui::PushStyleColor(ImGuiCol_Text, IM_COL32(130, 130, 130, 255));
    if (row.lineNumber >= 0)
    {
        ImGui::Text("%c %*d", RowMarker(row.kind), digits, row.lineNumber);
    }
    else
    {
        ImGui::Text("%c %*s", RowMarker(row.kind), digits, "");
    }

    ImGui::PopStyleColor();
    ImGui::SameLine();

    const ImVec2 textPos = ImGui::GetCursorScreenPos();
    if (hit.textLeft < 0.0f)
    {
        hit.textLeft = textPos.x;
    }

    /* Behind the text rather than over it, and the text keeps its own colour: what kind of change
     * a line is has to survive being selected. The table's own clip rectangle keeps a run wider
     * than the column inside it. */
    if (row.selectLength > 0)
    {
        const float cell = ImGui::CalcTextSize("M").x;
        const ImVec2 min(textPos.x + static_cast<float>(row.selectStart) * cell, textPos.y);
        ImGui::GetWindowDrawList()->AddRectFilled(
            min,
            ImVec2(
                min.x + static_cast<float>(row.selectLength) * cell,
                min.y + ImGui::GetTextLineHeight()),
            IM_COL32(55, 92, 130, 255));
    }

    ImGui::PushStyleColor(ImGuiCol_Text, RowColour(row.kind));
    RowText(screen, row, textPos);
    ImGui::PopStyleColor();
}

/* The row of the visible slice a y is over, clamped into it: a drag below the last row means the
 * last row rather than nothing. */
int RowAt(const PaneHit& hit, float y, int rowCount)
{
    if (rowCount <= 0)
    {
        return 0;
    }

    const float pitch = hit.pitch > 0.0f ? hit.pitch : ImGui::GetTextLineHeightWithSpacing();
    const int row = static_cast<int>((y - hit.first) / pitch);
    return std::min(std::max(row, 0), rowCount - 1);
}

/* Rounded to the nearest boundary between characters rather than truncated to the one under the
 * pointer, because a selection ends between two characters. Unclamped at the top: the managed side
 * holds the text and pulls it back to the end of the line there. */
int ColumnAt(const PaneHit& hit, float x, float cell)
{
    if (cell <= 0.0f)
    {
        return 0;
    }

    if (hit.textLeft < 0.0f)
    {
        return 0;
    }

    const int column = static_cast<int>((x - hit.textLeft) / cell + 0.5f);
    return std::max(column, 0);
}

/*
 * A drag across a pane, reduced to the two ends the managed side takes.
 *
 * Nothing here decides what is selected: the rows are reported in rows of the whole side, using
 * the scroll top the frame was drawn with, so a drag that spans a wheel notch still means what it
 * meant when it started.
 */
void UpdateSelection(
    const DeviewScreen* screen,
    const PaneHit& leftHit,
    const PaneHit& rightHit,
    const ImVec2& bodyMin,
    const ImVec2& bodyAvail,
    float dividerX,
    float cell)
{
    if (screen->paneCount < 2)
    {
        return;
    }

    /* A capture draws one frame in a fresh context that was never fed a mouse, so there is no
     * position to resolve anything against - and a pointer that left the window is the same
     * answer. */
    if (!ImGui::IsMousePosValid())
    {
        state.dragging = false;
        return;
    }

    const ImVec2 mouse = ImGui::GetIO().MousePos;
    if (!state.dragging)
    {
        if (!ImGui::IsMouseClicked(ImGuiMouseButton_Left) ||
            /* This head draws its own context menu, so a click on one lands on the panes as far as
             * anything here can tell. The other two heads use a real popup, whose tracking loop
             * swallows the click before a view ever sees it. */
            screen->menuCount > 0 ||
            mouse.y < bodyMin.y ||
            mouse.y > bodyMin.y + bodyAvail.y ||
            mouse.x > bodyMin.x + bodyAvail.x ||
            leftHit.cellLeft < 0.0f ||
            mouse.x < leftHit.cellLeft ||
            /* The splitter's grab zone overlaps the left pane's edge, and a drag that started
             * there would otherwise also select whatever it began over. */
            (dividerX >= 0.0f && mouse.x <= dividerX + grabWidth * Scale()))
        {
            return;
        }

        const bool right = rightHit.cellLeft >= 0.0f && mouse.x >= rightHit.cellLeft;
        const PaneHit& hit = right ? rightHit : leftHit;

        /* Nothing but filler on screen in the pane that was pressed, so nothing there to select.
         * Asked of that pane and of no other: asked of the left one whichever was pressed, a left
         * pane of filler ruled out the right pane's text with it, which is the whole of a pending
         * delete and wherever a long removed block has been scrolled to. */
        if (hit.textLeft < 0.0f)
        {
            return;
        }

        const DeviewPane& pane = screen->panes[right ? 1 : 0];
        state.dragging = true;
        state.dragSide = right ? 1 : 0;
        state.dragAnchorRow = pane.scrollTop + RowAt(hit, mouse.y, pane.rowCount);
        state.dragAnchorColumn = ColumnAt(hit, mouse.x, cell);
    }

    const PaneHit& hit = state.dragSide == 1 ? rightHit : leftHit;
    const DeviewPane& pane = screen->panes[state.dragSide == 1 ? 1 : 0];
    state.input.dragSide = state.dragSide;
    state.input.dragAnchorRow = state.dragAnchorRow;
    state.input.dragAnchorColumn = state.dragAnchorColumn;
    state.input.dragFocusRow = pane.scrollTop + RowAt(hit, mouse.y, pane.rowCount);
    state.input.dragFocusColumn = ColumnAt(hit, mouse.x, cell);

    /* Reported one last time on the frame the button came up, and then not at all: the managed
     * side is already holding the selection, so a release has nothing left to say. */
    if (!ImGui::IsMouseDown(ImGuiMouseButton_Left))
    {
        state.dragging = false;
    }
}

/*
 * Where a pane's picture goes, gathered from the table that drew the rows rather than recomputed.
 * The table owns the pane split, so asking it is the only way to place something under a column
 * that agrees with the column.
 */
struct PaneImage
{
    float left = 0.0f;
    float width = 0.0f;

    /*
     * The top of row zero and the pitch between rows. Together they put a picture one blank line
     * under the pane's own rows, which is the rule all three heads follow — and they are readable
     * from the first two rows rather than from a row past the pane's, which the table does not
     * always have.
     */
    float first = -1.0f;
    float pitch = 0.0f;
};

/* Called from inside the cell, which is the only place these are knowable. A picture still being
 * drawn needs them too, for the spinner that stands in for it. */
void RecordPaneImage(PaneImage& bounds, const DeviewPane& pane, int index)
{
    if ((pane.imagePathLength <= 0 && pane.imagePending == 0) ||
        index > 1)
    {
        return;
    }

    const ImVec2 cursor = ImGui::GetCursorScreenPos();
    if (index == 0)
    {
        bounds.left = cursor.x;
        bounds.width = ImGui::GetContentRegionAvail().x;
        bounds.first = cursor.y;
        return;
    }

    bounds.pitch = cursor.y - bounds.first;
}

/*
 * The checkerboard behind a picture, as one quad: the texture is two squares across and two down,
 * so texture coordinates that run to the picture's size over two squares repeat it at the size of
 * a square, counted from the picture's own top left corner.
 *
 * It was a quad a dark square, tessellated again every frame: about 4,500 for two pictures at the
 * size the window opens at and over 56,000 for two in a maximised 4K window, sixty times a second.
 * The pixels are the same ones. A square's edge falls on a whole pixel, half a pixel from the
 * nearest pixel centre either side, which is where the texture is sampled.
 */
void DrawChecker(ImDrawList* list, const ImVec2& min, const ImVec2& max)
{
    const Texture2D* checker = Checker();
    if (checker == nullptr)
    {
        list->AddRectFilled(min, max, IM_COL32(64, 64, 64, 255));
        return;
    }

    const float repeat = checkerSize * Scale() * 2.0f;
    list->AddImage(
        static_cast<ImTextureID>(checker->id),
        min,
        max,
        ImVec2(0.0f, 0.0f),
        ImVec2((max.x - min.x) / repeat, (max.y - min.y) / repeat));
}

/*
 * Something turning, centred where a picture will be once it is there to draw: a dim ring, and a
 * brighter quarter of it going round once a second, as the WinForms head draws it. Stood still in a
 * capture, which has to come out the same every time. Left out of a space too small to hold it.
 */
void DrawSpinner(ImDrawList* list, const ImVec2& centre, float pitch, float width, float height)
{
    const float radius = std::floor(pitch);
    const float thickness = std::max(2.0f, std::floor(pitch / 6.0f));
    if (width < (radius + thickness) * 2.0f ||
        height < (radius + thickness) * 2.0f)
    {
        return;
    }

    const float turned = state.capturing
        ? 0.0f
        : static_cast<float>(std::fmod(GetTime(), 1.0)) * 2.0f * IM_PI;
    list->AddCircle(centre, radius, IM_COL32(70, 70, 70, 255), 0, thickness);
    /* From twelve o'clock, clockwise: y grows downward, so a growing angle turns clockwise. */
    list->PathArcTo(centre, radius, turned - IM_PI * 0.5f, turned, 0);
    list->PathStroke(IM_COL32(130, 130, 130, 255), ImDrawFlags_None, thickness);
}

/*
 * The picture under a pane's rows. Absolutely positioned over the table rather than submitted as a
 * table row, because the rows a pane has and the rows the table has are different numbers: the
 * queue column is usually the tallest, and the space this fills is the pane's share of what the
 * queue is using.
 *
 * A spinner instead, while the picture is on its way: a document's page the managed side is still
 * drawing, which it says with imagePending, or a picture still being decoded here.
 */
void DrawPaneImage(const DeviewScreen* screen, const DeviewPane& pane, const PaneImage& bounds, float bottom, int side)
{
    /* A capture draws at a size of its own, and what it lays out is not where anything is in the
     * window the pointer is over. */
    State::PictureSpace unused;
    State::PictureSpace& space = state.capturing ? unused : state.pictureSpaces[side];
    space = State::PictureSpace{};

    const bool picture =
        pane.imagePathLength > 0 &&
        pane.imageWidth > 0 &&
        pane.imageHeight > 0;
    if ((!picture && pane.imagePending == 0) ||
        bounds.width <= 0.0f ||
        bounds.first < 0.0f)
    {
        return;
    }

    /* A table of one row has no second row to measure the pitch from: a one line document with
     * its page under it, in a window with no queue column. The line height RowAt falls back to. */
    const float pitch = bounds.pitch > 0.0f ? bounds.pitch : ImGui::GetTextLineHeightWithSpacing();
    const float top = bounds.first + static_cast<float>(pane.rowCount + 1) * pitch;
    const float available = bottom - top;
    if (available <= 0.0f)
    {
        return;
    }

    /* The whole space rather than the picture in it, and whether or not the picture has arrived: a
     * small picture is a small target, and a wheel turned beside it means the same thing. */
    space.present = true;
    space.left = bounds.left;
    space.top = top;
    space.width = bounds.width;
    space.height = available;

    ImDrawList* list = ImGui::GetWindowDrawList();
    const ImVec2 centre = ImFloor(ImVec2(bounds.left + bounds.width * 0.5f, top + available * 0.5f));
    if (!picture)
    {
        DrawSpinner(list, centre, pitch, bounds.width, available);
        return;
    }

    bool loading = false;
    const std::string path = Copy(screen, pane.imagePathOffset, pane.imagePathLength);
    const CachedTexture* decoded = Picture(path, loading);
    if (decoded == nullptr)
    {
        /* Nothing at all for a picture this build cannot decode: the rows have said what it is. */
        if (loading)
        {
            DrawSpinner(list, centre, pitch, bounds.width, available);
        }

        return;
    }

    /*
     * Fitted, and never enlarged past its own size: a snapshot is judged against the pixels it has,
     * and an eight pixel icon stretched across a pane is an interpolation of them rather than a
     * look at them.
     *
     * Scaled from the size the model carries rather than from the decoded texture, so all three
     * heads place a picture identically even where their decoders would not agree.
     */
    const float scale = std::min(
        std::min(
            bounds.width / static_cast<float>(pane.imageWidth),
            available / static_cast<float>(pane.imageHeight)),
        1.0f);
    const ImVec2 fitted(
        std::max(1.0f, static_cast<float>(pane.imageWidth) * scale),
        std::max(1.0f, static_cast<float>(pane.imageHeight) * scale));

    /*
     * Past that only by the reader asking, and then cut off at the edges of the space rather than
     * drawn over the rows above or the pane beside: what shows is the part around the centre the
     * managed side asked for, moved in as far as it takes to keep the space full. It does not know
     * how many pixels a pane has, so it can ask for one at the very edge.
     */
    ImVec2 size = fitted;
    ImVec2 uvMin(0.0f, 0.0f);
    ImVec2 uvMax(1.0f, 1.0f);
    const float drawnWidth = fitted.x * (pane.imageZoom > 1.0f ? pane.imageZoom : 1.0f);
    const float ownWidth = static_cast<float>(decoded->texture.width);
    Sample(
        path,
        pane.imageZoom > 1.0f && drawnWidth >= ownWidth ? sampleAsPixels :
        drawnWidth * 2.0f < ownWidth ? sampleReduced :
        sampleSmoothed);
    if (pane.imageZoom > 1.0f)
    {
        const ImVec2 whole(fitted.x * pane.imageZoom, fitted.y * pane.imageZoom);
        size = ImVec2(
            std::min(std::floor(bounds.width), std::max(1.0f, std::floor(whole.x))),
            std::min(std::floor(available), std::max(1.0f, std::floor(whole.y))));
        const float across = size.x / whole.x;
        const float down = size.y / whole.y;
        const float centreX = std::min(std::max(pane.imageCenterX, across * 0.5f), 1.0f - across * 0.5f);
        const float centreY = std::min(std::max(pane.imageCenterY, down * 0.5f), 1.0f - down * 0.5f);
        uvMin = ImVec2(centreX - across * 0.5f, centreY - down * 0.5f);
        uvMax = ImVec2(centreX + across * 0.5f, centreY + down * 0.5f);

        space.enlarged = true;
        space.wholeWidth = whole.x;
        space.wholeHeight = whole.y;
        space.centreX = centreX;
        space.centreY = centreY;
        space.across = across;
        space.down = down;
        space.movesAcross = std::floor(whole.x) > size.x;
        space.movesDown = std::floor(whole.y) > size.y;
    }

    /*
     * Snapped to the pixel grid: centring halves a difference of arbitrary floats, which is the
     * one place a half pixel can appear, and a picture drawn from a fractional origin rasterises
     * a row short with fattened borders. An unenlarged picture drawn from a whole pixel is its
     * own pixels, which is what the fit rule above is for.
     */
    const ImVec2 min = ImFloor(ImVec2(
        bounds.left + (bounds.width - size.x) * 0.5f,
        top + (available - size.y) * 0.5f));
    const ImVec2 max(min.x + size.x, min.y + size.y);

    /* Only behind a picture some of it would show through. Behind any other every square is under
     * an opaque pixel, and filling them costs a software rasteriser the picture's area again. */
    if (decoded->translucent)
    {
        DrawChecker(list, min, max);
    }

    list->AddImage(static_cast<ImTextureID>(decoded->texture.id), min, max, uvMin, uvMax);
    /* An outline, so a picture whose edges are the colour of the pane still has visible extent. */
    list->AddRect(
        ImVec2(min.x - 1.0f, min.y - 1.0f),
        ImVec2(max.x + 1.0f, max.y + 1.0f),
        IM_COL32(70, 70, 70, 255));
}

/* Whether a point is in the space either pane draws its picture in. */
bool OverPicture(float x, float y)
{
    for (const State::PictureSpace& space : state.pictureSpaces)
    {
        if (space.present &&
            x >= space.left &&
            x < space.left + space.width &&
            y >= space.top &&
            y < space.top + space.height)
        {
            return true;
        }
    }

    return false;
}

/*
 * An enlarged picture taken hold of and moved, reduced to the centre the managed side takes.
 *
 * Reported for as long as the button is held, as a drag across the text is, and from where the
 * button went down rather than from the frame before. Returns whether a drag is in progress, which
 * is what keeps the same press from also starting a selection.
 */
bool UpdatePan(const DeviewScreen* screen)
{
    if (!ImGui::IsMousePosValid())
    {
        state.panning = false;
        return false;
    }

    const ImVec2 mouse = ImGui::GetIO().MousePos;
    if (!state.panning)
    {
        if (!ImGui::IsMouseClicked(ImGuiMouseButton_Left) ||
            /* A click on this head's own context menu lands wherever the menu is floating. */
            screen->menuCount > 0)
        {
            return false;
        }

        for (int32_t side = 0; side < 2; side++)
        {
            const State::PictureSpace& space = state.pictureSpaces[side];
            if (space.present &&
                space.enlarged &&
                mouse.x >= space.left &&
                mouse.x < space.left + space.width &&
                mouse.y >= space.top &&
                mouse.y < space.top + space.height)
            {
                state.panning = true;
                state.panSide = side;
                state.panStart = mouse;
                state.panFrom = space;
                break;
            }
        }

        if (!state.panning)
        {
            return false;
        }
    }

    if (state.panSide >= screen->paneCount)
    {
        state.panning = false;
        return false;
    }

    /*
     * The picture follows the pointer, so the point at the middle moves the other way, as far as
     * this pane's picture can go.
     *
     * Only the way it can go at all. The centre is one point for both panes, and the two pictures
     * need not be the same shape: one that is all in view from top to bottom has nowhere to go
     * that way, and clamped like the other axis its report was the middle, every frame of the
     * drag. So dragging it sideways took the other pane's picture back to its middle row, from
     * wherever it had been dragged to. An axis this pane's picture cannot move on is reported as
     * the frame's own centre, the one the managed side handed over, which leaves it where it is.
     */
    const State::PictureSpace& from = state.panFrom;
    const DeviewPane& pane = screen->panes[state.panSide];
    const float x = from.centreX - (mouse.x - state.panStart.x) / from.wholeWidth;
    const float y = from.centreY - (mouse.y - state.panStart.y) / from.wholeHeight;
    state.input.panX = from.movesAcross
        ? std::min(std::max(x, from.across * 0.5f), 1.0f - from.across * 0.5f)
        : pane.imageCenterX;
    state.input.panY = from.movesDown
        ? std::min(std::max(y, from.down * 0.5f), 1.0f - from.down * 0.5f)
        : pane.imageCenterY;

    if (!ImGui::IsMouseDown(ImGuiMouseButton_Left))
    {
        state.panning = false;
    }

    return true;
}

/*
 * One line of text in no more than a width, ending in an ellipsis where it was cut short, so it
 * reads as cut rather than as all there was. It takes the place in the layout the text would.
 */
void TextWithin(const char* begin, const char* end, float width)
{
    const float room = std::max(width, 0.0f);
    const ImVec2 size = ImGui::CalcTextSize(begin, end);
    const ImVec2 position = ImGui::GetCursorScreenPos();
    const ImVec2 limit(position.x + room, position.y + size.y);
    ImGui::Dummy(ImVec2(std::min(size.x, room), size.y));
    ImGui::RenderTextEllipsis(ImGui::GetWindowDrawList(), position, limit, limit.x, begin, end, &size);
}

/*
 * ImGui reads a label for more than its text. From "##" on it is the item's identity and is not
 * drawn, which is how two buttons that say the same thing are told apart, and it is so for every
 * label an item takes: a queue row, a menu item, a button, a pane's header. Those are a file's
 * name, a test's, a solution's, and a name with "##" in it was cut short there: "Notes##2.txt" in
 * the queue was "Notes".
 *
 * A label is never an identity here, since every item is given one by its index. So one with the
 * mark in it is drawn by this side, where the item would have drawn it, over an item that is given
 * no text at all. One without it is left to the item, which is every label there has been.
 */
bool Marked(const std::string& label)
{
    return label.find("##") != std::string::npos;
}

void DrawLabel(const ImVec2& position, const std::string& label)
{
    ImGui::GetWindowDrawList()->AddText(
        position,
        ImGui::GetColorU32(ImGuiCol_Text),
        label.data(),
        label.data() + label.size());
}

bool SelectableLabel(const std::string& label, bool selected = false)
{
    if (!Marked(label))
    {
        return ImGui::Selectable(label.c_str(), selected);
    }

    /* Where Selectable puts its text: at the cursor, on the line's baseline. */
    const ImGuiWindow* window = ImGui::GetCurrentWindow();
    const ImVec2 position(window->DC.CursorPos.x, window->DC.CursorPos.y + window->DC.CurrLineTextBaseOffset);
    const bool pressed = ImGui::Selectable("##label", selected);
    DrawLabel(position, label);
    return pressed;
}

/* The width of a label's text, all of it. */
float LabelWidth(const std::string& label)
{
    return ImGui::CalcTextSize(label.data(), label.data() + label.size()).x;
}

bool ButtonLabel(const std::string& label)
{
    if (!Marked(label))
    {
        return ImGui::Button(label.c_str());
    }

    /* The size Button gives itself from its text, and the text where it puts it: inside the
     * frame's padding. */
    const ImGuiStyle& style = ImGui::GetStyle();
    const bool pressed = ImGui::Button(
        "##label",
        ImVec2(
            LabelWidth(label) + style.FramePadding.x * 2.0f,
            ImGui::GetTextLineHeight() + style.FramePadding.y * 2.0f));
    const ImVec2 corner = ImGui::GetItemRectMin();
    DrawLabel(ImVec2(corner.x + style.FramePadding.x, corner.y + style.FramePadding.y), label);
    return pressed;
}

/*
 * The header row of the panes' table, as TableHeadersRow lays it out, for a table with a marked
 * header: that one is given no text and has its own drawn over it, cut short with an ellipsis at
 * the column's edge as the table would have cut it.
 */
void HeadersRow(const std::string* headers, int first, int columns)
{
    ImGui::TableNextRow(ImGuiTableRowFlags_Headers, ImGui::TableGetHeaderRowHeight());
    for (int column = 0; column < columns; column++)
    {
        if (!ImGui::TableSetColumnIndex(column))
        {
            continue;
        }

        ImGui::PushID(column);
        if (column >= first &&
            Marked(headers[column - first]))
        {
            const std::string& header = headers[column - first];
            const ImVec2 position = ImGui::GetCursorScreenPos();
            ImGui::TableHeader("");
            const float edge = ImGui::TableGetCellBgRect(ImGui::GetCurrentTable(), column).Max.x;
            const ImVec2 size = ImGui::CalcTextSize(header.data(), header.data() + header.size());
            ImGui::RenderTextEllipsis(
                ImGui::GetWindowDrawList(),
                position,
                ImVec2(edge, position.y + size.y),
                edge,
                header.data(),
                header.data() + header.size(),
                &size);
        }
        else
        {
            ImGui::TableHeader(ImGui::TableGetColumnName(column));
        }

        ImGui::PopID();
    }
}

/*
 * How the footer is laid out: its buttons, on as many rows as the window's width makes of them,
 * and the status line, right aligned beside the last of those rows or on a line of its own.
 *
 * Worked out before the body is laid out, because the body is given what the footer leaves. A
 * footer that fits on one line is every footer there used to be, and is still laid out as it was:
 * each button after the one before, and the status after the last. That was the only layout, so
 * one that did not fit ran off the window. A paged document pending in a queue has eleven buttons,
 * 1199 pixels of them in a window with 1084: the last was past the window's edge, where it could
 * not be clicked, and the status past that, where it could not be read - and the status line is
 * where the page on screen, a page that could not be drawn, a selection and an accept that failed
 * are said.
 */
struct Footer
{
    std::vector<std::string> labels;

    /* Whether each button goes to the start of a new row rather than after the one before it. */
    std::vector<bool> wraps;

    std::string status;
    float statusWidth = 0.0f;

    /* On a line of its own under the buttons, for want of room beside the last row of them. */
    bool statusBelow = false;

    /* What all of it takes from the bottom of the window. */
    float height = 0.0f;
};

Footer LayOutFooter(const DeviewScreen* screen, float width)
{
    const ImGuiStyle& style = ImGui::GetStyle();
    Footer footer;
    int rows = 1;

    /* How far along its row the last button reaches. */
    float reach = 0.0f;
    for (int index = 0; index < screen->buttonCount; index++)
    {
        const DeviewButton& button = screen->buttons[index];
        footer.labels.push_back(Copy(screen, button.labelOffset, button.labelLength));

        /* What a button makes of a label: its text inside the frame's padding. */
        const float size = LabelWidth(footer.labels.back()) + style.FramePadding.x * 2.0f;
        const bool wraps = index > 0 && reach + style.ItemSpacing.x + size > width;
        footer.wraps.push_back(wraps);
        if (wraps)
        {
            rows++;
            reach = size;
        }
        else
        {
            reach += (index > 0 ? style.ItemSpacing.x : 0.0f) + size;
        }
    }

    footer.status = Copy(screen, screen->statusOffset, screen->statusLength);
    if (!footer.status.empty())
    {
        footer.statusWidth = ImGui::CalcTextSize(footer.status.c_str()).x;
        /* Beside the buttons only with room to spare, which is the test it was always put to. */
        footer.statusBelow =
            screen->buttonCount > 0 &&
            width - reach - style.ItemSpacing.x <= footer.statusWidth;
    }

    footer.height =
        static_cast<float>(rows) * ImGui::GetFrameHeightWithSpacing() + style.ItemSpacing.y +
        (footer.statusBelow ? ImGui::GetTextLineHeightWithSpacing() : 0.0f);
    return footer;
}

void BuildFrame(const DeviewScreen* screen)
{
    /* Read by the input pass, which has no screen of its own: Escape means dismiss while one of
     * these is up, and quit otherwise. */
    state.menuOpen = screen->menuCount > 0;

    const ImGuiViewport* viewport = ImGui::GetMainViewport();
    ImGui::SetNextWindowPos(viewport->WorkPos);
    ImGui::SetNextWindowSize(viewport->WorkSize);
    ImGui::Begin(
        "##deview",
        nullptr,
        ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove |
        ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoBringToFrontOnFocus |
        ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoScrollbar);

    /*
     * The title, which stops a character short of the subtitle where it would otherwise run on
     * under it: the subtitle is drawn in from the right edge wherever the title ended. It is the
     * title that gives way, because what it says is also in the pane headers and the queue, and
     * which entry of the queue this is is said only by the subtitle.
     */
    const std::string subtitle = Copy(screen, screen->subtitleOffset, screen->subtitleLength);
    const float subtitleWidth = subtitle.empty() ? 0.0f : ImGui::CalcTextSize(subtitle.c_str()).x;
    const float titleRoom = subtitle.empty()
        ? ImGui::GetContentRegionAvail().x
        : ImGui::GetContentRegionAvail().x - subtitleWidth - ImGui::CalcTextSize("M").x;
    const char* titleBegin;
    const char* titleEnd;
    if (Slice(screen, screen->titleOffset, screen->titleLength, &titleBegin, &titleEnd) &&
        ImGui::CalcTextSize(titleBegin, titleEnd).x > titleRoom)
    {
        TextWithin(titleBegin, titleEnd, titleRoom);
    }
    else
    {
        Text(screen, screen->titleOffset, screen->titleLength);
    }

    if (!subtitle.empty())
    {
        ImGui::SameLine(ImGui::GetContentRegionAvail().x - subtitleWidth);
        ImGui::TextDisabled("%s", subtitle.c_str());
    }

    ImGui::Separator();

    /*
     * Its height comes off the body, and the managed side is not asked for fewer rows to make up
     * for it. That side keeps eight lines for everything that is not a row, where this head's
     * title, headers and one line of footer take under five, so there are 62 pixels and more under
     * the last row it slices. A second row of buttons takes 23 of them and a line for the status
     * 17, and a third row of buttons on top of both is a pixel over at most. It is only past
     * that - four rows, which a paged document's buttons come to in a window under 450 pixels
     * wide - that the last rows of the body are cut off, behind a footer that can at least be read.
     */
    const Footer footer = LayOutFooter(screen, ImGui::GetContentRegionAvail().x);

    /*
     * The strip the pane scrollbar gets, taken off the body before anything is laid out in it.
     * Always reserved rather than appearing once a document outgrows the window: a strip that came
     * and went would shift the pane split every time the selection changed.
     */
    const float scrollbarWidth = ImGui::GetStyle().ScrollbarSize;
    ImGui::BeginChild("##body", ImVec2(-scrollbarWidth, -footer.height), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar);

    /* Read back rather than recomputed, so the scrollbar lands against the body whatever the
     * negative sizes above worked out as. */
    const ImVec2 bodyOrigin = ImGui::GetWindowPos();
    const ImVec2 bodyExtent = ImGui::GetWindowSize();

    const bool hasQueue = screen->queueCount > 0;
    const int columns = hasQueue ? 3 : 2;
    const float cell = ImGui::CalcTextSize("M").x;
    if (!state.capturing)
    {
        state.cellWidth = cell;
        state.lineHeight = ImGui::GetTextLineHeightWithSpacing();
    }

    ImVec2 menuAnchor;
    float menuRowTop = 0.0f;
    bool menuAnchored = false;
    if (!state.capturing &&
        state.queueWidth <= 0.0f)
    {
        state.queueWidth = cell * queueCells;
    }

    /* The body, measured before the table so the drag zone can span all of it rather than only the
     * rows the table happens to have. */
    const ImVec2 bodyMin = ImGui::GetCursorScreenPos();
    const ImVec2 bodyAvail = ImGui::GetContentRegionAvail();

    /* A capture's queue column is the width it starts at, in its own cells. The window's is in
     * the window's, which are larger on a scaled display, and may have been dragged. */
    const float queueWidth = ClampQueueWidth(
        state.capturing ? cell * queueCells : state.queueWidth,
        bodyAvail.x,
        cell);

    /* Where the border between the queue and the panes ended up, read back from the table rather
     * than recomputed, and -1 until a row has been laid out. */
    float dividerX = -1.0f;

    /* Gathered from the table, and used after it closes. Both stay empty on the overwhelmingly
     * common frame, where neither side is a picture. */
    PaneImage leftImage;
    PaneImage rightImage;

    const int digits = GutterDigits(screen);

    /* Filled by the same pass that draws the rows, and read after it by UpdateSelection. */
    PaneHit leftHit;
    PaneHit rightHit;
    if (screen->paneCount >= 2 &&
        ImGui::BeginTable("##panes", columns, ImGuiTableFlags_BordersInnerV | ImGuiTableFlags_SizingStretchSame))
    {
        const DeviewPane& left = screen->panes[0];
        const DeviewPane& right = screen->panes[1];
        if (hasQueue)
        {
            /* The count every other renderer puts in this header, with the column's id kept apart
             * from it so a count that changes is still the same column. */
            char pending[48];
            std::snprintf(pending, sizeof pending, "Pending (%d)###Pending", screen->pendingCount);
            ImGui::TableSetupColumn(pending, ImGuiTableColumnFlags_WidthFixed, queueWidth);
        }

        const std::string headers[] = {
            Copy(screen, left.headerOffset, left.headerLength),
            Copy(screen, right.headerOffset, right.headerLength)};
        ImGui::TableSetupColumn(headers[0].c_str());
        ImGui::TableSetupColumn(headers[1].c_str());
        if (Marked(headers[0]) ||
            Marked(headers[1]))
        {
            HeadersRow(headers, hasQueue ? 1 : 0, columns);
        }
        else
        {
            ImGui::TableHeadersRow();
        }

        int bodyRows = left.rowCount > right.rowCount ? left.rowCount : right.rowCount;
        if (screen->queueCount > bodyRows)
        {
            bodyRows = screen->queueCount;
        }

        for (int index = 0; index < bodyRows; index++)
        {
            ImGui::TableNextRow();
            int column = 0;
            if (hasQueue)
            {
                ImGui::TableSetColumnIndex(column++);
                if (index < screen->queueCount)
                {
                    const DeviewQueueItem& item = screen->queue[index];
                    std::string label = Copy(screen, item.labelOffset, item.labelLength);
                    if (item.flags & DEVIEW_QUEUE_HEADER)
                    {
                        /* A heading is dimmed like the subtitle, and never carries the selection.
                         * It is still a Selectable rather than plain text, because a left click on
                         * one folds its group: the hover it gains is the only thing on screen
                         * saying the marker can be clicked. */
                        ImGui::PushStyleColor(ImGuiCol_Text, ImGui::GetStyleColorVec4(ImGuiCol_TextDisabled));
                        ImGui::PushID(index);
                        if (SelectableLabel(label))
                        {
                            state.input.clickedQueueItem = index;
                        }

                        ImGui::PopID();
                        ImGui::PopStyleColor();
                    }
                    else
                    {
                        const bool selected = (item.flags & DEVIEW_QUEUE_SELECTED) != 0;
                        const bool failed = (item.flags & DEVIEW_QUEUE_FAILED) != 0;
                        if (failed)
                        {
                            ImGui::PushStyleColor(ImGuiCol_Text, RowColour(DEVIEW_ROW_REMOVED));
                            /* The marker the other three heads and docs/viewer.md show. Colour
                             * alone says nothing to a reader who cannot tell this red from the
                             * one a removed line is drawn in, or from any other. */
                            label += " !";
                        }

                        ImGui::PushID(index);
                        if (SelectableLabel(label, selected))
                        {
                            state.input.clickedQueueItem = index;
                        }

                        ImGui::PopID();
                        if (failed)
                        {
                            ImGui::PopStyleColor();
                        }
                    }

                    if (ImGui::IsItemClicked(ImGuiMouseButton_Right))
                    {
                        state.input.rightClickedQueueItem = index;
                    }

                    /* What the row cannot say for itself, composed by the managed side so the
                     * three heads cannot drift on it. An empty one means no tip at all rather than
                     * an empty popup: a tip that repeats its row has told the reader nothing. */
                    if (item.tooltipLength > 0 &&
                        ImGui::IsItemHovered(ImGuiHoveredFlags_DelayNormal))
                    {
                        const std::string tip = Copy(screen, item.tooltipOffset, item.tooltipLength);
                        if (!tip.empty())
                        {
                            ImGui::SetTooltip("%s", tip.c_str());
                        }
                    }
                    else if (item.tooltipLength > 0 &&
                             !state.capturing &&
                             ImGui::IsItemHovered())
                    {
                        /* Under the pointer and still waiting out the delay, which ImGui counts
                         * in the frames it is given and the time it is told each took. So frames
                         * have to keep coming with the pointer at rest, until the tip is up. */
                        state.tooltipDue = true;
                    }

                    if (index == screen->menuRow &&
                        screen->menuCount > 0)
                    {
                        menuAnchor = ImVec2(ImGui::GetItemRectMin().x, ImGui::GetItemRectMax().y);
                        menuRowTop = ImGui::GetItemRectMin().y;
                        menuAnchored = true;
                    }
                }
            }

            ImGui::TableSetColumnIndex(column);
            if (hasQueue && dividerX < 0.0f)
            {
                dividerX = ImGui::GetCursorScreenPos().x - ImGui::GetStyle().CellPadding.x;
            }

            RecordPaneImage(leftImage, left, index);
            DrawRow(screen, left, index, column, digits, leftHit);
            ImGui::TableSetColumnIndex(column + 1);
            RecordPaneImage(rightImage, right, index);
            DrawRow(screen, right, index, column + 1, digits, rightHit);
        }

        ImGui::EndTable();
    }

    if (screen->paneCount >= 2)
    {
        const float bottom = bodyMin.y + bodyAvail.y;
        DrawPaneImage(screen, screen->panes[0], leftImage, bottom, 0);
        DrawPaneImage(screen, screen->panes[1], rightImage, bottom, 1);
    }

    /* After the table, which is where the geometry it reads becomes complete, and before the
     * splitter, which claims its own clicks. A press that takes hold of an enlarged picture is not
     * also the start of a selection in the rows above it. */
    if (state.capturing ||
        !UpdatePan(screen))
    {
        UpdateSelection(screen, leftHit, rightHit, bodyMin, bodyAvail, dividerX, cell);
    }

    /*
     * A right-click anywhere in a pane, its text or under it, asks for the menu that copies from
     * it. Remembered here because the menu hangs where the click landed, and the managed side is
     * told which pane and nothing of where in it.
     *
     * Not a click on the menu already up, which floats over the panes and is drawn after them: the
     * queue rows take their own right-clicks as items, and a pane is not one.
     */
    if (!state.capturing &&
        screen->paneCount >= 2 &&
        ImGui::IsMousePosValid() &&
        ImGui::IsMouseClicked(ImGuiMouseButton_Right))
    {
        const ImVec2 mouse = ImGui::GetIO().MousePos;
        const bool overMenu =
            screen->menuCount > 0 &&
            mouse.x >= state.menuMin.x &&
            mouse.x < state.menuMax.x &&
            mouse.y >= state.menuMin.y &&
            mouse.y < state.menuMax.y;
        if (!overMenu &&
            leftHit.cellLeft >= 0.0f &&
            mouse.x >= leftHit.cellLeft &&
            mouse.x <= bodyMin.x + bodyAvail.x &&
            mouse.y >= bodyMin.y &&
            mouse.y <= bodyMin.y + bodyAvail.y)
        {
            state.input.rightClickedPane = rightHit.cellLeft >= 0.0f && mouse.x >= rightHit.cellLeft ? 1 : 0;
            state.paneMenuAnchor = mouse;
        }
    }

    /*
     * The drag, submitted after the table so it wins the overlap: within a window the last item to
     * claim a position is the one that hovers. Inert in a capture, which never feeds a mouse
     * button, so the width stays whatever this side decided.
     */
    if (dividerX >= 0.0f)
    {
        const ImVec2 resume = ImGui::GetCursorScreenPos();
        const float grab = grabWidth * Scale();
        ImGui::SetCursorScreenPos(ImVec2(dividerX - grab, bodyMin.y));
        ImGui::InvisibleButton(
            "##queue-splitter",
            ImVec2(grab * 2.0f + 1.0f, std::max(1.0f, bodyAvail.y)));
        if (ImGui::IsItemHovered() || ImGui::IsItemActive())
        {
            ImGui::SetMouseCursor(ImGuiMouseCursor_ResizeEW);
        }

        if (ImGui::IsItemActive())
        {
            /* Moved by the distance between the cursor and the border it is dragging, rather than
             * set from the cursor: a column's width is its inner width, and the border sits a
             * padding and a spacing further right. A delta needs to know neither. */
            state.queueWidth = ClampQueueWidth(
                queueWidth + ImGui::GetIO().MousePos.x - dividerX,
                bodyAvail.x,
                cell);
        }

        ImGui::SetCursorScreenPos(resume);
    }

    ImGui::EndChild();

    /*
     * The pane scrollbar, in the strip reserved above. Counted in rows rather than pixels: the
     * managed side clamps a scroll top to totalRows minus the rows on screen, and giving ImGui the
     * same two numbers makes the furthest the thumb can travel exactly that. Off by one here and
     * every drag to the bottom would land a row short and spring back.
     *
     * rowCount is the rows on screen: the slice is only shorter than the viewport when the scroll
     * top is past the clamp, which the managed side does not allow, so it equals the viewport in
     * every state that can be reached and equals totalRows when the whole document fits.
     */
    if (screen->paneCount >= 1)
    {
        const DeviewPane& scrolled = screen->panes[0];
        ImS64 scroll = scrolled.scrollTop;
        const ImRect bounds(
            ImVec2(bodyOrigin.x + bodyExtent.x, bodyOrigin.y),
            ImVec2(bodyOrigin.x + bodyExtent.x + scrollbarWidth, bodyOrigin.y + bodyExtent.y));
        if (ImGui::ScrollbarEx(
                bounds,
                ImGui::GetID("##panescroll"),
                ImGuiAxis_Y,
                &scroll,
                scrolled.rowCount > 0 ? scrolled.rowCount : 1,
                scrolled.totalRows,
                ImDrawFlags_None))
        {
            state.input.scrollTo = static_cast<int32_t>(scroll);
        }
    }

    ImGui::Separator();

    /*
     * The context menu, its own floating window so it draws over the panes. The managed side owns
     * opening and closing; this only draws what the screen carries and reports a clicked item.
     */
    const bool paneMenu = screen->menuPane >= 0 && screen->paneCount >= 2;
    if (screen->menuCount > 0 && (menuAnchored || paneMenu))
    {
        /* Sized by hand rather than AlwaysAutoResize, which measures during its first frame and
         * so draws nothing on it — and a pixel capture is exactly one frame. */
        std::vector<std::string> labels;
        float widest = 0.0f;
        for (int index = 0; index < screen->menuCount; index++)
        {
            const DeviewMenuItem& item = screen->menu[index];
            labels.push_back(Copy(screen, item.labelOffset, item.labelLength));
            widest = std::max(widest, ImGui::CalcTextSize(labels.back().c_str()).x);
        }

        const ImGuiStyle& style = ImGui::GetStyle();
        const ImVec2 size(
            widest + style.WindowPadding.x * 2.0f + cell,
            static_cast<float>(screen->menuCount) * ImGui::GetTextLineHeightWithSpacing() +
                style.WindowPadding.y * 2.0f);
        ImVec2 position(menuAnchor.x + cell, menuAnchor.y);
        if (paneMenu)
        {
            /* Where the pointer was when it was asked for. A capture was never fed a pointer, so
             * there it hangs from the top of the pane it is for, which is somewhere that is the
             * same every time. */
            const PaneHit& hit = screen->menuPane == 1 ? rightHit : leftHit;
            position = state.capturing
                ? ImVec2(hit.cellLeft + cell, bodyMin.y + ImGui::GetTextLineHeightWithSpacing())
                : state.paneMenuAnchor;
        }

        /* Kept inside the window, whichever it hangs from: a click near a pane's right or bottom
         * edge would otherwise hang most of the menu off it, and under the last rows of a queue
         * that fills its column there is not the height of a menu left. A row's menu goes over
         * the row then rather than under it, so the row it is about can still be read. */
        const ImVec2 display = ImGui::GetIO().DisplaySize;
        if (!paneMenu &&
            position.y + size.y > display.y)
        {
            position.y = menuRowTop - size.y;
        }

        position.x = std::max(0.0f, std::min(position.x, display.x - size.x));
        position.y = std::max(0.0f, std::min(position.y, display.y - size.y));

        state.menuMin = position;
        state.menuMax = ImVec2(position.x + size.x, position.y + size.y);
        ImGui::SetNextWindowPos(position);
        ImGui::SetNextWindowSize(size);
        ImGui::PushStyleColor(ImGuiCol_WindowBg, IM_COL32(28, 28, 28, 255));
        ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 1.0f);
        ImGui::Begin(
            "##contextmenu",
            nullptr,
            ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove |
            ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoSavedSettings |
            ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoFocusOnAppearing);
        for (int index = 0; index < screen->menuCount; index++)
        {
            ImGui::PushID(index);
            if (SelectableLabel(labels[static_cast<size_t>(index)]))
            {
                state.input.clickedMenuItem = index;
            }

            ImGui::PopID();
        }

        /* Asked before End, which is what makes it about this window. A click anywhere else is a
         * dismissal: the menu used to float until a row, a button or a key was hit, contrary to
         * what docs/viewer.md says of it. A right click elsewhere opens the next menu, and the
         * managed side ignores a dismissal that arrives with one of those. */
        const bool overMenu = ImGui::IsWindowHovered(ImGuiHoveredFlags_ChildWindows);

        ImGui::End();
        ImGui::PopStyleVar();
        ImGui::PopStyleColor();

        if (!overMenu &&
            (ImGui::IsMouseClicked(ImGuiMouseButton_Left) ||
             ImGui::IsMouseClicked(ImGuiMouseButton_Right)))
        {
            state.input.menuClosed = 1;
        }
    }

    for (int index = 0; index < screen->buttonCount; index++)
    {
        const DeviewButton& button = screen->buttons[index];
        const bool enabled = (button.flags & DEVIEW_BUTTON_ENABLED) != 0;
        if (index > 0 &&
            !footer.wraps[static_cast<size_t>(index)])
        {
            ImGui::SameLine();
        }

        if (!enabled)
        {
            ImGui::BeginDisabled();
        }

        ImGui::PushID(index);
        if (ButtonLabel(footer.labels[static_cast<size_t>(index)]))
        {
            state.input.clickedButton = index;
        }

        ImGui::PopID();
        if (!enabled)
        {
            ImGui::EndDisabled();
        }
    }

    if (!footer.status.empty())
    {
        if (!footer.statusBelow)
        {
            ImGui::SameLine();
        }

        const float available = ImGui::GetContentRegionAvail().x;
        if (available > footer.statusWidth)
        {
            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + available - footer.statusWidth);
            ImGui::TextDisabled("%s", footer.status.c_str());
        }
        else
        {
            /* Wider than the window even with a line to itself. From the left edge then, so that
             * what is lost is its end, and said to be lost. */
            ImGui::PushStyleColor(ImGuiCol_Text, ImGui::GetStyleColorVec4(ImGuiCol_TextDisabled));
            TextWithin(footer.status.data(), footer.status.data() + footer.status.size(), available);
            ImGui::PopStyleColor();
        }
    }

    ImGui::End();
}

/* ---- which frames are drawn ---- */

void Append(std::vector<unsigned char>& bytes, const void* data, size_t size)
{
    if (data == nullptr ||
        size == 0)
    {
        return;
    }

    const unsigned char* begin = static_cast<const unsigned char*>(data);
    bytes.insert(bytes.end(), begin, begin + size);
}

/* An array of the screen with its count ahead of it, so two screens whose arrays are the same
 * bytes in all but split differently are not the same screen. */
template <typename Element>
void AppendArray(std::vector<unsigned char>& bytes, const Element* elements, int32_t count)
{
    Append(bytes, &count, sizeof count);
    if (count > 0)
    {
        Append(bytes, elements, static_cast<size_t>(count) * sizeof(Element));
    }
}

/*
 * Whether the screen handed over differs from the one handed over last, which is kept either way.
 *
 * By its bytes, every one of them: the managed side builds each screen into the same buffers, so
 * where they point says nothing, and none of the structs has padding to differ for no reason. The
 * same bytes are the same strings, rows, panes, queue and menu, and so the same frame for as long
 * as nothing else that a frame is built from has moved.
 */
bool Changed(const DeviewScreen* screen)
{
    std::vector<unsigned char>& bytes = state.arriving;
    bytes.clear();
    AppendArray(bytes, screen->strings, screen->stringsLength);
    AppendArray(bytes, screen->panes, screen->paneCount);
    AppendArray(bytes, screen->rows, screen->rowCount);
    AppendArray(bytes, screen->segments, screen->segmentCount);
    AppendArray(bytes, screen->buttons, screen->buttonCount);
    AppendArray(bytes, screen->queue, screen->queueCount);
    AppendArray(bytes, screen->menu, screen->menuCount);
    const int32_t rest[] = {
        screen->pendingCount,
        screen->titleOffset,
        screen->titleLength,
        screen->subtitleOffset,
        screen->subtitleLength,
        screen->statusOffset,
        screen->statusLength,
        screen->menuRow,
        screen->menuPane};
    Append(bytes, rest, sizeof rest);

    if (bytes == state.presented)
    {
        return false;
    }

    bytes.swap(state.presented);
    return true;
}

/*
 * Whether anything has come from the pointer, the keys or the window since the last present: what
 * raylib gathered when it last read the window system's events, which is what the frame about to
 * be built would be given.
 *
 * A key is not something a frame is built from, since ImGui is given none: what a key does comes
 * back from the managed side as a different screen. It counts all the same, because a window is
 * only left alone when nothing at all is happening to it. By the press rather than by what is
 * down, since raylib reports Caps Lock and Num Lock as held for as long as they are on.
 *
 * The buttons, the wheel and the keys are asked of what GLFW's callbacks kept rather than of
 * raylib, for the reason they are kept: see State::presses.
 */
bool Arrived()
{
    bool arrived = false;

    const Vector2 pointer = GetMousePosition();
    if (pointer.x != state.pointer.x ||
        pointer.y != state.pointer.y)
    {
        state.pointer = pointer;
        arrived = true;
    }

    /* A press or a release waiting to be handed to ImGui, a button held, or one of a press and
     * release handed over together that ImGui is keeping for the frame after. */
    if (!state.presses.empty() ||
        state.held[0] ||
        state.held[1] ||
        state.held[2] ||
        state.context->InputEventsQueue.Size > 0)
    {
        arrived = true;
    }

    if (state.wheelAcross != 0.0f ||
        state.wheelDown != 0.0f)
    {
        arrived = true;
    }

    /* The pointer leaving the window, or coming back to where it left from: raylib's position
     * for it is the same before and after. */
    const bool gone =
        !state.pointerInside &&
        !state.held[0] &&
        !state.held[1] &&
        !state.held[2];
    if (gone != state.pointerGone)
    {
        state.pointerGone = gone;
        arrived = true;
    }

    if (state.keyed)
    {
        state.keyed = false;
        arrived = true;
    }

    const int width = GetScreenWidth();
    const int height = GetScreenHeight();
    const bool hidden = IsWindowState(FLAG_WINDOW_HIDDEN);
    const bool minimised = IsWindowMinimized();
    const bool focused = IsWindowFocused();
    if (width != state.width ||
        height != state.height ||
        hidden != state.hidden ||
        minimised != state.minimised)
    {
        /* A window of another size has another framebuffer, and one that was not on the screen
         * was not being kept by anything. */
        state.stale = true;
        arrived = true;
    }

    if (focused != state.focused)
    {
        arrived = true;
    }

    state.width = width;
    state.height = height;
    state.hidden = hidden;
    state.minimised = minimised;
    state.focused = focused;
    return arrived;
}

uint64_t Mix(uint64_t hash, const void* data, size_t size)
{
    const unsigned char* bytes = static_cast<const unsigned char*>(data);
    for (; size >= sizeof(uint64_t); bytes += sizeof(uint64_t), size -= sizeof(uint64_t))
    {
        uint64_t word;
        memcpy(&word, bytes, sizeof word);
        hash = (hash ^ word) * 0x9E3779B97F4A7C15ull;
        hash ^= hash >> 29;
    }

    for (; size > 0; bytes++, size--)
    {
        hash = (hash ^ *bytes) * 0x100000001B3ull;
    }

    return hash;
}

/*
 * Everything RenderDrawData would draw a frame from, reduced to one number: the size drawn at, and
 * for every draw list its vertices, its indices and what each command clips to and draws with.
 *
 * Two frames with the same number are the same pixels, short of a texture's content having changed
 * behind its name, which is asked separately. That is what lets a frame be built and then not
 * drawn: it is held against the frame on the screen, and a frame that would put the same pixels
 * there again is a full window for a software rasteriser to fill, and for the window system to
 * copy, to no effect. Sixty four bits, so two frames that differ share a number about as often as
 * never, and a pass over the vertices costs a small part of what drawing them would.
 */
uint64_t Fingerprint(const ImDrawData* drawData)
{
    uint64_t hash = 0xCBF29CE484222325ull;
    hash = Mix(hash, &drawData->DisplaySize, sizeof drawData->DisplaySize);
    for (int list = 0; list < drawData->CmdListsCount; list++)
    {
        const ImDrawList* commands = drawData->CmdLists[list];
        hash = Mix(
            hash,
            commands->VtxBuffer.Data,
            static_cast<size_t>(commands->VtxBuffer.Size) * sizeof(ImDrawVert));
        hash = Mix(
            hash,
            commands->IdxBuffer.Data,
            static_cast<size_t>(commands->IdxBuffer.Size) * sizeof(ImDrawIdx));
        for (const ImDrawCmd& command : commands->CmdBuffer)
        {
            const uint64_t drawn[] = {
                static_cast<uint64_t>(command.GetTexID()),
                command.VtxOffset,
                command.IdxOffset,
                command.ElemCount};
            hash = Mix(hash, &command.ClipRect, sizeof command.ClipRect);
            hash = Mix(hash, drawn, sizeof drawn);
        }
    }

    return hash;
}

/*
 * Whether ImGui is waiting for a texture to be made, updated or destroyed, which RenderDrawData
 * does as it draws: the font atlas, when a character is drawn for the first time. Such a frame is
 * drawn whatever its fingerprint, so the atlas on the GPU never falls behind the one ImGui holds.
 */
bool TexturesWaiting(const ImDrawData* drawData)
{
    if (drawData->Textures == nullptr)
    {
        return false;
    }

    for (const ImTextureData* texture : *drawData->Textures)
    {
        if (texture->Status != ImTextureStatus_OK)
        {
            return true;
        }
    }

    return false;
}

/*
 * The end of every frame, drawn or not: what EndDrawing does once a frame is on the screen, which
 * it can no longer be left to do, since it only does it for a frame it has put there.
 *
 * It waits out what is left of the frame, counted from when the last one's wait ended, so the
 * managed loop turns sixty times a second whatever a turn drew: without the wait a window with
 * nothing to draw would spin a core. And then it reads the window system's events, last, so that
 * what deview_poll_input reports and what the next frame is built from are as fresh as they can
 * be. That is the order raylib kept them in.
 */
void Rest()
{
    const double left = frameSeconds - (GetTime() - state.ended);
    if (left > 0.0)
    {
        /* Never longer than a frame, whatever the clock has done. */
        WaitTime(std::min(left, frameSeconds));
    }

    state.characterFollows = false;
    PollInputEvents();
    state.ended = GetTime();
}

void ApplyStyle()
{
    ImGuiStyle& style = ImGui::GetStyle();
    ImGui::StyleColorsDark();
    style.WindowRounding = 0.0f;
    style.WindowBorderSize = 0.0f;
    style.WindowPadding = ImVec2(8.0f, 6.0f);
    style.FramePadding = ImVec2(8.0f, 3.0f);
    style.ItemSpacing = ImVec2(6.0f, 2.0f);
    style.CellPadding = ImVec2(6.0f, 1.0f);
    style.ScrollbarSize = 12.0f;
}
}

extern "C"
{
int32_t deview_version(void)
{
    return DEVIEW_VERSION;
}

int32_t deview_init(
    int32_t width,
    int32_t height,
    const char* title,
    const uint8_t* fontTtf,
    int32_t fontLength,
    float fontSize,
    int32_t hidden)
{
    if (state.initialised)
    {
        return 1;
    }

    SetTraceLogLevel(LOG_WARNING);
    /* No MSAA. ImGui draws axis aligned quads with pre-antialiased glyph textures, so multisampling
     * buys nothing visually, and it is a real source of difference between a GPU and the software
     * rasteriser the pixel snapshots are pinned to. */
    /* ALWAYS_RUN because WindowShouldClose waits on events while the window is minimised, and
     * that call is inside deview_present: without it the managed loop stops being pumped the
     * moment the window is minimised, so a snapshot arriving after that is accepted by the
     * listener and never shown. */
    unsigned int flags = FLAG_WINDOW_RESIZABLE | FLAG_WINDOW_ALWAYS_RUN;
    if (hidden != 0)
    {
        flags |= FLAG_WINDOW_HIDDEN;
    }

    /* At the size it was left, which is known before there is a window to ask about monitors. Where
     * it goes is decided below, once there is. */
    const bool sized = state.placed &&
                       hidden == 0 &&
                       state.placement.width > 0 &&
                       state.placement.height > 0;
    SetConfigFlags(flags);
    InitWindow(
        sized ? state.placement.width : width,
        sized ? state.placement.height : height,
        title == nullptr ? "DiffEngineViewer" : title);
    if (!IsWindowReady())
    {
        return 0;
    }

    void* handle = glfwGetCurrentContext();

    /* Not under 1: a desktop set smaller than a pixel to the pixel is not asking for text below
     * the size it is legible at. And not a number that is no scale at all. */
    float across = 1.0f;
    float down = 1.0f;
    glfwGetWindowContentScale(handle, &across, &down);
    state.scale = across > 1.0f && across <= 8.0f ? across : 1.0f;
    state.cellWidth = 0.0f;
    state.lineHeight = 0.0f;

    state.tracked = false;
    if (!sized &&
        state.scale != 1.0f)
    {
        /* The size asked for is in the pixels of an ordinary display. As much of it at this
         * display's scale as its monitor has room for, and in the middle of that monitor, which is
         * where raylib put the window it has just made at the size it was given. There is no
         * asking the scale before there is a window to ask it of. */
        const int monitor = GetCurrentMonitor();
        const int wide = std::min(static_cast<int>(static_cast<float>(width) * state.scale), GetMonitorWidth(monitor));
        const int tall = std::min(static_cast<int>(static_cast<float>(height) * state.scale), GetMonitorHeight(monitor));
        const Vector2 origin = GetMonitorPosition(monitor);
        SetWindowSize(wide, tall);
        SetWindowPosition(
            static_cast<int>(origin.x) + (GetMonitorWidth(monitor) - wide) / 2,
            static_cast<int>(origin.y) + (GetMonitorHeight(monitor) - tall) / 2);
    }

    if (sized)
    {
        if (OnAMonitor(state.placement))
        {
            SetWindowPosition(state.placement.x, state.placement.y);
        }

        /* What it restores to, noted before maximising takes the chance away. */
        TrackPlacement();
        if (state.placement.maximized != 0)
        {
            MaximizeWindow();
        }
    }

    SetExitKey(KEY_NULL);

    /* No SetTargetFPS: raylib only holds to it inside EndDrawing, which is no longer called. The
     * frame is ended, and waited out, by Rest. And nothing about a window that came before this
     * one says anything about this one, whose clock has started again from nothing. */
    glfwSetWindowRefreshCallback(handle, WindowRefreshed);
    state.raylibCrossing = glfwSetCursorEnterCallback(handle, PointerCrossed);
    state.pointerInside = true;
    state.pointerGone = false;
    state.raylibButton = glfwSetMouseButtonCallback(handle, ButtonChanged);
    state.raylibScroll = glfwSetScrollCallback(handle, WheelTurned);
    state.raylibKey = glfwSetKeyCallback(handle, KeyChanged);
    state.raylibCharacter = glfwSetCharCallback(handle, CharacterTyped);
    state.presses.clear();
    state.keys.clear();
    state.held[0] = state.held[1] = state.held[2] = false;
    state.wheelAcross = 0.0f;
    state.wheelDown = 0.0f;
    state.wheelNotches = 0.0f;
    state.scrollRemainder = 0.0f;
    state.characterFollows = false;
    state.keyed = false;
    state.presented.clear();
    state.watched.clear();
    state.shown = 0;
    state.stale = true;
    state.settled = 0;
    state.tooltipDue = false;
    state.began = 0.0;
    state.ended = 0.0;
    state.pointer = Vector2{};
    state.width = 0;
    state.height = 0;

    state.context = ImGui::CreateContext();
    ImGui::SetCurrentContext(state.context);
    ImGuiIO& io = ImGui::GetIO();
    io.BackendFlags |= ImGuiBackendFlags_RendererHasTextures;
    /* Declared, so ImGui splits a long draw list into commands with a vertex offset rather than
     * refusing to let one grow past what a sixteen bit index can address. RenderTriangles applies
     * the offset. */
    io.BackendFlags |= ImGuiBackendFlags_RendererHasVtxOffset;
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    ApplyStyle();
    if (state.scale != 1.0f)
    {
        /* The window's context alone. A capture makes its own, which is left at 1. */
        ImGuiStyle& style = ImGui::GetStyle();
        style.ScaleAllSizes(state.scale);
        style.FontScaleDpi = state.scale;
    }

    if (fontTtf != nullptr && fontLength > 0)
    {
        /*
         * Twice. The first is the atlas's default, and so what a capture's context draws with:
         * the embedded font and nothing else, on every machine. The second is the window's, which
         * the machine's fonts are merged into, and it is added last because a merge goes into
         * the font added before it. See State::font.
         */
        AddEmbeddedFont(fontTtf, fontLength, fontSize);
        state.font = AddEmbeddedFont(fontTtf, fontLength, fontSize);
        io.FontDefault = state.font;
    }

    ResetInput();
    state.initialised = true;
    state.windowOpen = true;
    return 1;
}

int32_t deview_present(const DeviewScreen* screen)
{
    if (!state.initialised || !state.windowOpen || screen == nullptr)
    {
        return 0;
    }

    if (WindowShouldClose())
    {
        /* Reported once to the managed side, which decides between hiding and exiting depending
         * on whether a tray is running. Cleared immediately so a hidden window can be shown again
         * rather than closing itself on its first frame back. */
        state.input.closeRequested = 1;
        ClearCloseFlag();
    }

    ImGui::SetCurrentContext(state.context);

    const double now = GetTime();
    const float elapsed = state.began > 0.0 && now > state.began
        ? static_cast<float>(now - state.began)
        : static_cast<float>(frameSeconds);
    state.began = now;

    /*
     * What has arrived since the last present. Each of these is asked every time, whatever the
     * ones before it said, since each also takes what it finds.
     *
     * Before the frame asks for its pictures, so one that finished decoding since the last frame
     * is drawn in this one.
     */
    bool arrived = TakeDecoded();
    /* And its fonts, for that reason and because a font can only be added between frames. */
    arrived = TakeFonts() || arrived;
    const bool changed = Changed(screen);
    arrived = changed || arrived;
    arrived = Arrived() || arrived;
    arrived = arrived || state.tooltipDue;

    /*
     * A hidden window is not built for and not drawn into, whatever has arrived. It was, each time
     * its screen changed, and a viewer hidden behind a tray is handed another screen by every
     * arrival in its queue: the whole queue laid out and the whole window filled, for nobody.
     *
     * What arrived has still been taken in, above, and the fonts for what the screen holds are
     * still asked for, so that they are there by the time it is shown. Showing it is itself an
     * arrival, and marks the window stale, so the first present after it builds the screen it is
     * handed and draws it: see deview_set_hidden and Arrived.
     *
     * Not before a frame has been built for the window at all. The grid the managed side slices
     * its rows by is measured from one, and ImGui has no font to measure with until its first.
     */
    if (state.hidden &&
        state.cellWidth > 0.0f)
    {
        if (changed)
        {
            FindFontsFor(screen);
        }

        Rest();
        MeasureGrid();
        TrackPlacement();
        return 1;
    }

    /*
     * A window nothing is happening to is left alone: no frame is built, and nothing is drawn.
     * It used to be built, drawn and put on the screen sixty times a second, each one the frame
     * already there, and under a software rasteriser or over a remote session each of those is the
     * whole window filled and copied again.
     *
     * Left alone only once it is certain the next frame would be the one on the screen. The screen
     * handed over is the last one byte for byte, nothing has come from the pointer, the keys, the
     * window system, the decoder or the font finder, no tooltip is waiting to appear, the files
     * behind the pictures are as they were, and a second of frames built since any of that last
     * changed have all come out as the frame on the screen. That last is what a spinner fails,
     * and anything else that moves by itself.
     */
    if (!arrived &&
        !state.stale &&
        state.settled >= settledFrames)
    {
        if (!PicturesRewritten())
        {
            Rest();
            MeasureGrid();
            TrackPlacement();
            return 1;
        }

        arrived = true;
    }

    PumpInput(elapsed);
    FindFontsFor(screen);
    state.watched.clear();
    state.tooltipDue = false;
    ImGui::NewFrame();
    BuildFrame(screen);
    ImGui::Render();

    /* ImGui only records the cursor it wants. Showing it is the backend's job, and the splitter is
     * the one thing here that asks for anything but an arrow. */
    const int cursor = ImGui::GetMouseCursor() == ImGuiMouseCursor_ResizeEW
        ? MOUSE_CURSOR_RESIZE_EW
        : MOUSE_CURSOR_DEFAULT;
    if (cursor != state.cursor)
    {
        state.cursor = cursor;
        SetMouseCursor(cursor);
    }

    /*
     * Drawn only if it is not the frame on the screen: the pointer crossing a pane, a key that did
     * nothing and the frames that follow any change mostly come out as the frame before them.
     * Building one costs a fraction of drawing it, and is what all of ImGui's own state is kept
     * moving by, so those frames are built and not drawn rather than not built.
     */
    ImDrawData* drawData = ImGui::GetDrawData();
    const uint64_t frame = Fingerprint(drawData);
    if (state.stale ||
        frame != state.shown ||
        TexturesWaiting(drawData))
    {
        BeginDrawing();
        ClearBackground(Color{24, 24, 24, 255});
        RenderDrawData(drawData);
        rlDrawRenderBatchActive();
        SwapScreenBuffer();
        state.shown = frame;
        state.stale = false;
        state.settled = 0;
    }
    else if (arrived)
    {
        state.settled = 0;
    }
    else if (state.settled < settledFrames)
    {
        state.settled++;
    }

    Rest();
    ForgetUnusedPictures();

    MeasureGrid();
    TrackPlacement();
    return 1;
}

void deview_poll_input(DeviewInput* input)
{
    if (input == nullptr)
    {
        return;
    }

    if (state.initialised)
    {
        bool escape = false;
        state.input.key = ReadKey(escape);

        /* Escape with a menu up dismisses the menu. It reached the managed side as quit, which
         * closes the menu and then runs the command, so Esc-to-dismiss closed the viewer - and on
         * Linux there is no tray to open it again from, so the queue went to staging. */
        if (state.menuOpen &&
            escape)
        {
            state.input.key = DEVIEW_KEY_NONE;
            state.input.menuClosed = 1;
        }

        /* Whole notches, keeping the fraction. A touchpad sends a fraction of one per frame and
         * truncating each frame on its own threw every one of them away. Every wheel message since
         * the last poll, added up: read off raylib it was the last of them alone. */
        state.scrollRemainder += state.wheelNotches;
        state.wheelNotches = 0.0f;
        const int32_t notches = static_cast<int32_t>(state.scrollRemainder);
        state.scrollRemainder -= static_cast<float>(notches);

        /* Over a picture the wheel is for the picture, and anywhere with control held, as it is in
         * everything else that shows one. Everywhere else it scrolls the rows, as it always has. */
        const Vector2 mouse = GetMousePosition();
        const bool control =
            IsKeyDown(KEY_LEFT_CONTROL) || IsKeyDown(KEY_RIGHT_CONTROL) ||
            IsKeyDown(KEY_LEFT_SUPER) || IsKeyDown(KEY_RIGHT_SUPER);
        if (control || OverPicture(mouse.x, mouse.y))
        {
            state.input.zoomDelta = notches;
        }
        else
        {
            state.input.scrollDelta = notches;
        }

        MeasureGrid();
    }

    *input = state.input;
    ResetInput();
}

int32_t deview_capture(const DeviewScreen* screen, int32_t width, int32_t height, const char* pngPath)
{
    if (!state.initialised || screen == nullptr || pngPath == nullptr)
    {
        return 0;
    }

    /*
     * A fresh context per capture, sharing the live context's font atlas. ImGui carries layout
     * state between frames — tables and windows remember their previous geometry — so a capture
     * drawn in the live context inherits whatever the frame before it left there, and what that
     * was depends on which capture ran before this one. A context that exists for exactly one
     * frame has no previous frame, so a capture is a function of the screen model alone.
     */
    ImGui::SetCurrentContext(state.context);
    ImFontAtlas* atlas = ImGui::GetIO().Fonts;
    ImGuiContext* capture = ImGui::CreateContext(atlas);
    ImGui::SetCurrentContext(capture);
    ImGuiIO& io = ImGui::GetIO();
    io.BackendFlags |= ImGuiBackendFlags_RendererHasTextures;
    /* As the window's context declares it, and for its reason: both are drawn by RenderTriangles.
     * Without it a capture of more than 65,535 vertices in one draw list, which dense text at 4K
     * comes to, had its indices wrap, and came out scrambled. */
    io.BackendFlags |= ImGuiBackendFlags_RendererHasVtxOffset;
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    ApplyStyle();
    io.DisplaySize = ImVec2(static_cast<float>(width), static_cast<float>(height));
    io.DeltaTime = 1.0f / 60.0f;

    RenderTexture2D target = LoadRenderTexture(width, height);
    if (!IsRenderTextureValid(target))
    {
        ImGui::DestroyContext(capture);
        ImGui::SetCurrentContext(state.context);
        return 0;
    }

    ImGui::NewFrame();
    state.capturing = true;
    BuildFrame(screen);
    state.capturing = false;
    ImGui::Render();

    BeginTextureMode(target);
    ClearBackground(Color{24, 24, 24, 255});
    RenderDrawData(ImGui::GetDrawData());
    EndTextureMode();
    ForgetUnusedPictures();

    Image image = LoadImageFromTexture(target.texture);
    /* Render textures come back bottom up. */
    ImageFlipVertical(&image);
    const bool exported = ExportImage(image, pngPath);
    UnloadImage(image);
    UnloadRenderTexture(target);
    ImGui::DestroyContext(capture);
    ImGui::SetCurrentContext(state.context);
    ResetInput();
    return exported ? 1 : 0;
}

void deview_set_hidden(int32_t hidden)
{
    if (!state.initialised)
    {
        return;
    }

    if (hidden != 0)
    {
        SetWindowState(FLAG_WINDOW_HIDDEN);
        return;
    }

    ClearWindowState(FLAG_WINDOW_HIDDEN);
    /* The window system asks for a window it has just shown to be drawn. Not waited for: nothing
     * was keeping what a hidden window showed. */
    state.stale = true;
}

void deview_set_clipboard(const char* text)
{
    /* GLFW owns the clipboard and needs its window, so a runtime that never opened one - a capture
     * host - copies nothing rather than crashing. */
    if (text == nullptr ||
        !state.initialised ||
        !state.windowOpen)
    {
        return;
    }

    SetClipboardText(text);
}

void deview_focus(void)
{
    if (!state.initialised)
    {
        return;
    }

    ClearWindowState(FLAG_WINDOW_HIDDEN);
    /* A minimised window stays minimised through SetWindowFocused, so a focus for a new snapshot
     * left it in the taskbar. */
    if (IsWindowMinimized())
    {
        RestoreWindow();
    }

    SetWindowFocused();
    /* Shown, restored or raised: drawn again, as in deview_set_hidden. */
    state.stale = true;
}

void deview_set_placement(const DeviewPlacement* placement)
{
    if (placement == nullptr)
    {
        return;
    }

    state.placement = *placement;
    state.placed = true;
}

int32_t deview_get_placement(DeviewPlacement* placement)
{
    if (placement == nullptr ||
        !state.initialised ||
        !state.windowOpen ||
        !state.tracked)
    {
        return 0;
    }

    *placement = state.normal;
    return 1;
}

void deview_shutdown(void)
{
    if (!state.initialised)
    {
        return;
    }

    /* Before CloseWindow, which takes the GL context these live in with it. */
    UnloadPictures();
    StopFontFinder();

    if (state.context != nullptr)
    {
        ImGui::SetCurrentContext(state.context);
        ImGui::DestroyContext(state.context);
        state.context = nullptr;
    }

    /* After the context, whose atlas was still reading glyphs out of these. */
    state.font = nullptr;
    state.fontData.clear();
    state.asked.clear();

    state.presented.clear();
    state.arriving.clear();
    state.watched.clear();

    CloseWindow();
    state.initialised = false;
    state.windowOpen = false;
}
}
