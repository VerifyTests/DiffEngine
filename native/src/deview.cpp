/*
 * DiffEngineViewer native renderer: raylib for the window and GL, Dear ImGui for the widgets.
 *
 * The ImGui backend here is deliberately minimal. Panes do not scroll inside ImGui, because the
 * managed side already slices each frame to the visible rows, so there is no scroll state to keep
 * in sync and no keyboard navigation to wire up. That leaves only three things a backend must do:
 * honour texture requests, feed mouse input, and turn ImDrawData into rlgl calls.
 */
#include "deview.h"
#include "platformheads/runtime.h"

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
 * And GLFW's way to an entry point of the GL the window was made with, for the one thing asked of
 * GL that rlgl has no call for: how large a texture it will take. See TextureLimit.
 */
extern "C"
{
typedef void (*DeviewGlEntry)(void);
DeviewGlEntry glfwGetProcAddress(const char* name);
}

namespace
{
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
 * The lines the managed side keeps for everything that is not a row of the body, which it takes
 * off the rows it is told the window has. Keep in sync with ScreenBuilder.Chrome.
 */
constexpr int chromeRows = 8;

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

    /*
     * A texture is made with reduced copies of the picture only for a picture that is drawn at
     * under three quarters of its size, which is the only one sampled from them (see WantsCopies):
     * they are a third again of a picture's memory, and a picture that is no larger than its pane
     * is never drawn that small. One that comes to be, in a
     * window made smaller, is decoded a second time with them while the texture it has goes on
     * being drawn, which is what `reducing` says is on its way. And `unreducible` is a picture
     * that came back from that without them, which is not asked again. See Reduce.
     */
    bool reducing = false;
    bool unreducible = false;
};

/*
 * One picture to decode, or decoded: the path, the stamp the decode was asked for, whether its
 * reduced copies are wanted with it, and once it is done the pixels, which are empty when raylib
 * could not read the file, and whether any of them can be seen through.
 */
struct Decode
{
    std::string path;
    std::uintmax_t length = 0;
    std::filesystem::file_time_type written{};
    bool reduced = false;
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

struct State
{
    bool initialised = false;
    bool windowOpen = false;
    ImGuiContext* context = nullptr;
    DeviewInput input{};

    /* A character cell's width and a row's height in the last frame built for the window, which
     * is what the grid reported to the managed side is counted in: see MeasureGrid. */
    float cellWidth = 0.0f;
    float lineHeight = 0.0f;

    /* How many rows the body had room for in the last frame built for the window, above whatever
     * footer that frame laid out, or -1 before there has been one: see MeasureGrid. */
    int32_t bodyRows = -1;

    /* Whether the last screen carried a context menu, which is what makes Escape and a click
     * outside it a dismissal rather than what they would otherwise mean. */
    bool menuOpen = false;

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

        /* The centre the frame asked for, before it was moved in to keep this space full: one
         * point for both panes, so it can be somewhere this pane cannot show and the other can. */
        float askedX = 0.5f;
        float askedY = 0.5f;

        /* Whether there is more of it across, and down, than the space shows: whether the space
         * cut it short that way, which is the only way it can be moved. */
        bool movesAcross = false;
        bool movesDown = false;
    };

    PictureSpace pictureSpaces[2];

    /*
     * An enlarged picture being dragged: where the button went down, and how the picture was placed
     * then, which the whole drag is measured from. Measured from the last frame instead, a drag
     * would drift by whatever each frame's clamp took off it. And how the other pane's was placed
     * then, since how far that one can go is part of how far the drag can take the centre the two
     * share.
     */
    bool panning = false;
    int32_t panSide = 0;
    ImVec2 panStart{};
    PictureSpace panFrom{};
    PictureSpace panOther{};

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
     * What decides whether a frame is put on the screen, and whether one is built at all: see
     * deview_present. First the screen as it was last handed over, every byte of it, and the one
     * being held against it.
     */
    std::vector<unsigned char> presented;
    std::vector<unsigned char> arriving;

    /* A queue row's tooltip is waiting out its delay, which ImGui counts in the frames it is
     * given: see BuildFrame. */
    bool tooltipDue = false;

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
 * capture's. See ph::WindowScale. */
float Scale()
{
    return state.capturing ? 1.0f : ph::WindowScale();
}

/* What the runtime does for every head, under the names this file has always called them by. */
using ph::ButtonLabel;
using ph::HeadersRow;
using ph::LabelWidth;
using ph::LetterOf;
using ph::Marked;
using ph::RenderDrawData;
using ph::SelectableLabel;
using ph::TextWithin;

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

/* Every string is an offset into one UTF-8 blob, read by the runtime: see ph::Slice. */
bool Slice(const DeviewScreen* screen, int offset, int length, const char** begin, const char** end)
{
    return ph::Slice(screen->strings, screen->stringsLength, offset, length, begin, end);
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
    return ph::Copy(screen->strings, screen->stringsLength, offset, length);
}

/* Asks for a font for every character of a screen the window's font cannot draw. Every string
 * of the frame is in the one blob, so one pass over it covers the title, the queue and the
 * tooltips with the rows. */
void FindFontsFor(const DeviewScreen* screen)
{
    if (screen->strings != nullptr &&
        screen->stringsLength > 0)
    {
        ph::FindFontsFor(reinterpret_cast<const char*>(screen->strings), static_cast<size_t>(screen->stringsLength));
    }
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
 * The four ways a picture's texture is sampled, by the size it is drawn at.
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
 *
 * And from whichever one of those copies is nearest in size, between half its size and three
 * quarters. Under about seven tenths that is the half size copy, in which a grid of one pixel
 * lines is even, where smoothed between its own pixels at two thirds of its size some of its lines
 * are dark and some faint. From seven tenths up it is the picture itself, and the same pixels as
 * smoothed.
 *
 * The last is what raylib makes of "bilinear" for any texture that has reduced copies, and every
 * picture not under half its size was sampled that way, at its own size as well. Which copy is
 * worked out for every pixel drawn, and where the answer is always the picture itself that was a
 * tenth of a frame of two pictures under a software rasteriser, for nothing. So smoothed is set
 * here by the filters themselves, and is asked for from three quarters up: clear of the seven
 * tenths where the answer changes, so what is drawn is what was drawn at every size.
 */
constexpr int sampleSmoothed = 0;
constexpr int sampleAsPixels = 1;
constexpr int sampleReduced = 2;
constexpr int sampleNearestCopy = 3;

/* Whether a picture drawn so wide is sampled from its reduced copies, either way, and so wants a
 * texture that has them: under three quarters of the width it has. */
bool WantsCopies(float drawnWidth, float ownWidth)
{
    return drawnWidth * 4.0f < ownWidth * 3.0f;
}

int SamplingOf(float drawnWidth, float ownWidth, bool enlarged)
{
    return enlarged && drawnWidth >= ownWidth ? sampleAsPixels :
           drawnWidth * 2.0f < ownWidth ? sampleReduced :
           WantsCopies(drawnWidth, ownWidth) ? sampleNearestCopy :
           sampleSmoothed;
}

void Filter(const Texture2D& texture, int sampling)
{
    rlTextureParameters(
        texture.id,
        RL_TEXTURE_MIN_FILTER,
        sampling == sampleAsPixels ? RL_TEXTURE_FILTER_NEAREST :
        sampling == sampleReduced ? RL_TEXTURE_FILTER_MIP_LINEAR :
        sampling == sampleNearestCopy ? RL_TEXTURE_FILTER_LINEAR_MIP_NEAREST :
        RL_TEXTURE_FILTER_LINEAR);
    rlTextureParameters(
        texture.id,
        RL_TEXTURE_MAG_FILTER,
        sampling == sampleAsPixels ? RL_TEXTURE_FILTER_NEAREST : RL_TEXTURE_FILTER_LINEAR);
}

/*
 * How a picture's texture is sampled as it is made: smoothed. Clamped at its edges rather than
 * repeating, which is raylib's default: sampled at its last column, a repeating texture takes in
 * its first, and a picture that is opaque on the left and clear on the right grew a line of its
 * left edge down its right.
 */
void PrepareTexture(CachedTexture& entry)
{
    Filter(entry.texture, sampleSmoothed);
    SetTextureWrap(entry.texture, TEXTURE_WRAP_CLAMP);
    entry.sampling = sampleSmoothed;
}

/* Changed only when it has to be, since it is a texture parameter and this is asked every
 * frame. A picture whose reduced copies could not be made, or have yet to land, is smoothed
 * instead. */
void Sample(const std::string& path, int sampling)
{
    const auto found = state.pictures.find(path);
    if (found == state.pictures.end() ||
        !found->second.loaded)
    {
        return;
    }

    CachedTexture& entry = found->second;
    if ((sampling == sampleReduced || sampling == sampleNearestCopy) &&
        entry.texture.mipmaps <= 1)
    {
        sampling = sampleSmoothed;
    }

    if (entry.sampling == sampling)
    {
        return;
    }

    Filter(entry.texture, sampling);
    entry.sampling = sampling;
}

/*
 * The longest side of a texture the window's GL will take, or zero where it would not say.
 *
 * A picture past it cannot be a texture as it is. Handed to GL all the same, it came back as a
 * texture with a name and no pixels, which draws as black: a box of it where the picture should
 * be. It is 16384 under Mesa's software rasteriser, and a screenshot of the whole of a long page
 * is past that. Such a picture is brought down to the limit as it is read: see FitToATexture.
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
 * A picture longer on a side than a texture can be, resampled to the largest size of its own shape
 * that a texture can: every pixel of what is left an average of the ones it stands for, as in a
 * reduced copy, so a thin line is fainter there and not gone.
 *
 * It was drawn as nothing, and a screenshot of the whole of a long page is such a picture. What
 * the pane then shows is not its pixels, at any zoom: the rows still give the size it has, and
 * enlarged far enough it is the pixels of this copy.
 *
 * On the decoder's thread, for every picture but a capture's, because of what it costs. The copy
 * is made beside the picture as it was read, so for as long as that takes both are held: a
 * picture 20,000 pixels square with no alpha channel is 1.2 GB read, and 0.8 GB more for a copy
 * 16,384 square before the first is given back. Measured where the limit is that, on one thread:
 * that picture took 2.1 s to read and 1.2 s more to bring down, one 17,000 by 9,000 with an
 * alpha channel 0.4 s and 0.9 s, and a screenshot 1,920 by 30,000 with one 0.3 s and 0.2 s.
 */
void FitToATexture(Image& image, int limit)
{
    if (image.data == nullptr ||
        FitsATexture(image, limit))
    {
        return;
    }

    const double factor = static_cast<double>(limit) / static_cast<double>(std::max(image.width, image.height));
    ImageResize(
        &image,
        std::min(limit, std::max(1, static_cast<int>(std::lround(image.width * factor)))),
        std::min(limit, std::max(1, static_cast<int>(std::lround(image.height * factor)))));
}

/*
 * A picture read off the disk and made ready to be a texture: whether any of it can be seen
 * through, brought down to a size a texture can hold if it is past that, and its reduced copies
 * where they are wanted. All of it here because here is off the window's thread for every picture
 * but a capture's. The file, stb_image and raylib's resampling, and nothing that touches GL.
 */
Image ReadPicture(const std::string& path, int textureLimit, bool reduced, bool& translucent)
{
    Image image = LoadImage(path.c_str());
    translucent = SeeThrough(image);
    FitToATexture(image, textureLimit);
    if (reduced &&
        image.data != nullptr &&
        FitsATexture(image, textureLimit))
    {
        ImageMipmaps(&image);
    }

    return image;
}

/*
 * The texture for a picture that has been read, or false for one there is none for: a file that
 * could not be read, a picture still longer on a side than a texture can be, which is one read
 * against another limit than this context's, or a context that would not make one. On the thread
 * that owns the context.
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

/*
 * Puts a texture with reduced copies in the place of the one a picture has, from the picture read
 * a second time with them. One that came back without them, or that no texture could be made of,
 * leaves the picture with the texture it had, smoothed, and is not asked for again.
 */
bool ReplaceTexture(const Image& image, bool translucent, CachedTexture& entry)
{
    CachedTexture made;
    if (image.mipmaps <= 1 ||
        !MakeTexture(image, translucent, made))
    {
        entry.unreducible = true;
        return false;
    }

    UnloadTexture(entry.texture);
    entry.texture = made.texture;
    entry.translucent = made.translucent;
    entry.sampling = made.sampling;

    /* Another texture behind the picture, which may have been given the name of the last. */
    ph::MarkStale();
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

        decode.image = ReadPicture(decode.path, decoder->textureLimit, decode.reduced, decode.translucent);

        lock.lock();
        if (decoder->stopping)
        {
            UnloadImage(decode.image);
            return;
        }

        decoder->done.push_back(std::move(decode));
    }
}

void RequestDecode(const std::string& path, std::uintmax_t length, std::filesystem::file_time_type written, bool reduced)
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
        decode.reduced = reduced;
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
                ph::MarkStale();
            }
        }
        else if (found != state.pictures.end() &&
                 found->second.reducing &&
                 /* Not a decode without them that an entry forgotten since had asked for. */
                 decode.reduced &&
                 found->second.written == decode.written &&
                 found->second.length == decode.length)
        {
            found->second.reducing = false;
            landed = ReplaceTexture(decode.image, decode.translucent, found->second) || landed;
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

    if (found->second.decoding ||
        found->second.reducing)
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
 * Asks for the reduced copies of a picture whose texture was made without them, now that a frame
 * draws it small enough to want them. Off the window's thread, as the picture was decoded, with
 * the texture it has drawn smoothed until they land: making them here would hold the window for
 * as long as a large picture takes to be halved over and over. A capture has no later frame for
 * them to land in, and makes them there and then, as it decodes.
 */
void Reduce(const std::string& path, CachedTexture& entry)
{
    if (!entry.loaded ||
        entry.texture.mipmaps > 1 ||
        entry.unreducible)
    {
        return;
    }

    if (state.capturing)
    {
        bool translucent = false;
        const Image image = ReadPicture(path, TextureLimit(), true, translucent);
        ReplaceTexture(image, translucent, entry);
        UnloadImage(image);
        return;
    }

    if (entry.reducing)
    {
        return;
    }

    entry.reducing = true;
    RequestDecode(path, entry.length, entry.written, true);
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
 *
 * `reduced` is whether the frame draws it small enough to want its reduced copies: see
 * CachedTexture::reducing.
 */
const CachedTexture* Picture(const std::string& path, bool reduced, bool& loading)
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
            if (reduced)
            {
                Reduce(path, found->second);
            }

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
        const Image image = ReadPicture(path, TextureLimit(), reduced, translucent);
        MakeTexture(image, translucent, entry);
        UnloadImage(image);
    }
    else
    {
        entry.decoding = true;
        loading = true;
        RequestDecode(path, length, written, reduced);
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

        if (entry->second.decoding ||
            entry->second.reducing)
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

/* ---- input ---- */

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
int Pressed(const ph::KeyPress& press)
{
    const unsigned int letter = LetterOf(press);

    /* Super as well as control, so a macOS keyboard driving the Linux build through a remote
     * session still copies with the chord its user has in their fingers. */
    if ((press.mods & (ph::ModControl | ph::ModSuper)) != 0)
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
    const bool shift = (press.mods & ph::ModShift) != 0;

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
int KeyOf(const ph::KeyPress& press)
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
    ph::KeyPress press;
    while (ph::PopKey(press))
    {
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
 *
 * And no more rows than the body has room for, with the lines the managed side takes off for
 * everything else added back. That side keeps eight lines where this head's title, headers and one
 * line of footer take under five, so a footer of two or three lines fits in what is over and the
 * window's height in rows is the answer, as it always was. A taller one does not: a paged
 * document's buttons come to four rows in a window under 450 pixels wide, and the body is given
 * what the footer leaves, so the last rows the managed side sliced were under it. They are taken
 * off here instead, counted from where the last frame put the body's first row and its bottom.
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
    if (state.bodyRows >= 0)
    {
        state.input.rows = std::min(state.input.rows, state.bodyRows + chromeRows);
    }
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
    const float drawnWidth = fitted.x * (pane.imageZoom > 1.0f ? pane.imageZoom : 1.0f);

    /* Asked for with its reduced copies only if it is drawn small enough to be sampled from
     * them, which is known before it is decoded, from the size the model carries. */
    bool loading = false;
    const std::string path = Copy(screen, pane.imagePathOffset, pane.imagePathLength);
    const CachedTexture* decoded = Picture(path, WantsCopies(drawnWidth, static_cast<float>(pane.imageWidth)), loading);
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
     * Past that only by the reader asking, and then cut off at the edges of the space rather than
     * drawn over the rows above or the pane beside: what shows is the part around the centre the
     * managed side asked for, moved in as far as it takes to keep the space full. It does not know
     * how many pixels a pane has, so it can ask for one at the very edge.
     */
    ImVec2 size = fitted;
    ImVec2 uvMin(0.0f, 0.0f);
    ImVec2 uvMax(1.0f, 1.0f);
    const float ownWidth = static_cast<float>(decoded->texture.width);
    Sample(path, SamplingOf(drawnWidth, ownWidth, pane.imageZoom > 1.0f));
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
        space.askedX = pane.imageCenterX;
        space.askedY = pane.imageCenterY;
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

/* A centre moved in as far as it takes for a space showing so much of its picture to stay full. */
float KeptCentre(float centre, float shown)
{
    return std::min(std::max(centre, shown * 0.5f), 1.0f - shown * 0.5f);
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
                state.panOther = state.pictureSpaces[1 - side];
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
     * The picture follows the pointer, so the point at the middle moves the other way.
     *
     * Only the way it can go at all. The centre is one point for both panes, and the two pictures
     * need not be the same shape: one that is all in view from top to bottom has nowhere to go
     * that way, and clamped like the other axis its report was the middle, every frame of the
     * drag. So dragging it sideways took the other pane's picture back to its middle row, from
     * wherever it had been dragged to. An axis this pane's picture cannot move on is reported as
     * the frame's own centre, the one the managed side handed over, which leaves it where it is.
     *
     * And on an axis both can move on, as far as the one that can go further. What is moved is
     * the centre the frame asked for, not the one this pane drew about, and it is kept inside what
     * the pane that shows less of its picture can show. Moved from this pane's own and kept to
     * this pane's range, the first move of a drag brought the other pane's picture in from
     * wherever beyond that range it had been dragged to.
     */
    const State::PictureSpace& from = state.panFrom;
    const State::PictureSpace& other = state.panOther;
    const DeviewPane& pane = screen->panes[state.panSide];
    const float across = other.enlarged && other.movesAcross ? std::min(from.across, other.across) : from.across;
    const float down = other.enlarged && other.movesDown ? std::min(from.down, other.down) : from.down;
    state.input.panX = from.movesAcross
        ? KeptCentre(KeptCentre(from.askedX, across) - (mouse.x - state.panStart.x) / from.wholeWidth, across)
        : pane.imageCenterX;
    state.input.panY = from.movesDown
        ? KeptCentre(KeptCentre(from.askedY, down) - (mouse.y - state.panStart.y) / from.wholeHeight, down)
        : pane.imageCenterY;

    if (!ImGui::IsMouseDown(ImGuiMouseButton_Left))
    {
        state.panning = false;
    }

    return true;
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
     * Its height comes off the body. The managed side keeps eight lines for everything that is not
     * a row, where this head's title, headers and one line of footer take under five, so there are
     * 62 pixels and more under the last row it slices. A second row of buttons takes 23 of them
     * and a line for the status 17, and a third row of buttons on top of both is a pixel over at
     * most. Past that - four rows, which a paged document's buttons come to in a window under 450
     * pixels wide - the managed side is asked for fewer rows: see MeasureGrid.
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

    if (!state.capturing)
    {
        /* What MeasureGrid holds the rows it reports to: how many of them there is room for
         * between the body's first and its bottom, which is where the footer begins. Unknown for
         * a frame with no rows, which says nothing about where one would be. */
        const float pitch = leftHit.pitch > 0.0f ? leftHit.pitch : ImGui::GetTextLineHeightWithSpacing();
        state.bodyRows = leftHit.first >= 0.0f && pitch > 0.0f
            ? std::max(0, static_cast<int32_t>((bodyMin.y + bodyAvail.y - leftHit.first) / pitch))
            : -1;
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

void deview_on_display_lost(DeviewDisplayLost lost)
{
    ph::OnDisplayLost(lost);
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

    /* Made hidden, placed where it was left and then shown, by the runtime: see ph::OpenWindow. A
     * viewer started behind a tray comes up where it was left too, which it did not when the
     * placement was only used for a window made visible. */
    ph::WindowOptions options;
    options.width = width;
    options.height = height;
    options.title = title == nullptr ? "DiffEngineViewer" : title;
    options.hidden = hidden != 0;
    ph::Placement remembered;
    if (state.placed)
    {
        remembered.x = state.placement.x;
        remembered.y = state.placement.y;
        remembered.width = state.placement.width;
        remembered.height = state.placement.height;
        remembered.maximized = state.placement.maximized != 0;
        options.placement = &remembered;
    }

    if (!ph::OpenWindow(options))
    {
        return 0;
    }

    state.cellWidth = 0.0f;
    state.lineHeight = 0.0f;
    state.presented.clear();
    state.watched.clear();
    state.tooltipDue = false;

    state.context = ImGui::CreateContext();
    ImGui::SetCurrentContext(state.context);
    ImGuiIO& io = ImGui::GetIO();
    io.BackendFlags |= ImGuiBackendFlags_RendererHasTextures;
    /* Declared, so ImGui splits a long draw list into commands with a vertex offset rather than
     * refusing to let one grow past what a sixteen bit index can address. ph::RenderDrawData
     * applies the offset. */
    io.BackendFlags |= ImGuiBackendFlags_RendererHasVtxOffset;
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    ApplyStyle();
    const float scale = ph::WindowScale();
    if (scale != 1.0f)
    {
        /* The window's context alone. A capture makes its own, which is left at 1. */
        ImGuiStyle& style = ImGui::GetStyle();
        style.ScaleAllSizes(scale);
        style.FontScaleDpi = scale;
    }

    if (fontTtf != nullptr && fontLength > 0)
    {
        /*
         * Twice. The first is the atlas's default, and so what a capture's context draws with:
         * the embedded font and nothing else, on every machine. The second is the window's, which
         * the machine's fonts are merged into, and it is added last because a merge goes into the
         * font added before it. So a capture is the same picture on a machine with every font
         * installed and on one with none, and what the two draw from the embedded font is the
         * same glyphs, so the captures still describe the window.
         */
        ph::AddEmbeddedFont(fontTtf, fontLength, fontSize, emScale);
        ImFont* font = ph::AddEmbeddedFont(fontTtf, fontLength, fontSize, emScale);
        io.FontDefault = font;
        ph::UseSystemFonts(font);
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

    /* Reported once to the managed side, which decides between hiding and exiting depending on
     * whether a tray is running. Cleared at once, so a hidden window can be shown again rather
     * than closing itself on its first frame back. */
    if (ph::TakeCloseRequest())
    {
        state.input.closeRequested = 1;
    }

    const float elapsed = ph::BeginPresent(state.context);

    /*
     * What has arrived since the last present. Each of these is asked every time, whatever the
     * ones before it said, since each also takes what it finds.
     *
     * Before the frame asks for its pictures, so one that finished decoding since the last frame
     * is drawn in this one.
     */
    bool arrived = TakeDecoded();
    /* And its fonts, for that reason and because a font can only be added between frames. */
    arrived = ph::TakeFonts() || arrived;
    const bool changed = Changed(screen);
    arrived = changed || arrived;
    arrived = ph::Arrived() || arrived;
    arrived = arrived || state.tooltipDue;

    /*
     * A hidden or minimised window is not built for and not drawn into, whatever has arrived. It
     * was, each time its screen changed, and a viewer hidden behind a tray is handed another screen
     * by every arrival in its queue: the whole queue laid out and the whole window filled, for
     * nobody.
     *
     * What arrived has still been taken in, above, and the fonts for what the screen holds are
     * still asked for, so that they are there by the time it is shown. Showing it is itself an
     * arrival, and marks the window stale, so the first present after it builds the screen it is
     * handed and draws it.
     *
     * Not before a frame has been built for the window at all. The grid the managed side slices
     * its rows by is measured from one, and ImGui has no font to measure with until its first.
     */
    if (ph::Hidden() &&
        state.cellWidth > 0.0f)
    {
        if (changed)
        {
            FindFontsFor(screen);
        }

        ph::Rest();
        MeasureGrid();
        return 1;
    }

    /*
     * A window nothing is happening to is left alone: no frame is built, and nothing is drawn.
     * It used to be built, drawn and put on the screen sixty times a second, each one the frame
     * already there, and under a software rasteriser or over a remote session each of those is the
     * whole window filled and copied again.
     *
     * Left alone only once it is certain the next frame would be the one on the screen: see
     * ph::CanLeaveAlone. And the files behind the pictures are as they were, which only this head
     * can say.
     */
    if (ph::CanLeaveAlone(arrived))
    {
        if (!PicturesRewritten())
        {
            ph::Rest();
            MeasureGrid();
            return 1;
        }

        arrived = true;
    }

    /* No keys for ImGui: every key is read here and handed to the managed side, and none of this
     * head's widgets takes text. */
    ph::FeedImGui(elapsed, false);
    FindFontsFor(screen);
    state.watched.clear();
    state.tooltipDue = false;
    ImGui::NewFrame();
    BuildFrame(screen);
    ImGui::Render();

    /* Drawn only if it is not the frame on the screen: building one costs a fraction of drawing
     * it, and is what all of ImGui's own state is kept moving by, so those frames are built and
     * not drawn rather than not built. */
    ph::Show(ImGui::GetDrawData(), 24, 24, 24, arrived);
    ph::Rest();
    ForgetUnusedPictures();

    MeasureGrid();
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

        /* Every wheel message since the last poll, in whole notches with the fraction kept: see
         * ph::TakeWheelNotches. */
        const int32_t notches = ph::TakeWheelNotches();

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

        /* As it is now, like the grid: see ph::Unseen. */
        state.input.unseen = ph::Unseen() ? 1 : 0;
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
    /* As the window's context declares it, and for its reason. Without it a capture of more than
     * 65,535 vertices in one draw list, which dense text at 4K comes to, had its indices wrap, and
     * came out scrambled. */
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

    /* Shown, a minimised window is brought back too: see ph::SetHidden. */
    ph::SetHidden(hidden != 0);
}

void deview_set_clipboard(const char* text)
{
    /* GLFW owns the clipboard and needs its window, so a runtime that never opened one - a capture
     * host - copies nothing rather than crashing. */
    if (!state.initialised ||
        !state.windowOpen)
    {
        return;
    }

    ph::SetClipboard(text);
}

void deview_focus(void)
{
    if (!state.initialised)
    {
        return;
    }

    ph::Focus();
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
    ph::Placement normal;
    if (placement == nullptr ||
        !state.initialised ||
        !state.windowOpen ||
        !ph::GetPlacement(normal))
    {
        return 0;
    }

    placement->x = normal.x;
    placement->y = normal.y;
    placement->width = normal.width;
    placement->height = normal.height;
    placement->maximized = normal.maximized ? 1 : 0;
    return 1;
}

void deview_shutdown(void)
{
    if (!state.initialised)
    {
        return;
    }

    /* Before the window closes, which takes the GL context these live in with it. */
    UnloadPictures();

    if (state.context != nullptr)
    {
        ImGui::SetCurrentContext(state.context);
        ImGui::DestroyContext(state.context);
        state.context = nullptr;
    }

    /* After the context, whose atlas was still reading glyphs out of the fonts merged in. */
    ph::StopFontFinder();

    state.presented.clear();
    state.arriving.clear();
    state.watched.clear();

    ph::CloseWindow();
    state.initialised = false;
    state.windowOpen = false;
}
}
