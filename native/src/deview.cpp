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
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <filesystem>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <system_error>
#include <thread>
#include <vector>

/*
 * raylib latches GLFW's close flag and exposes no way to clear it, but the window has to survive a
 * close when a tray is running, otherwise every later frame would report closing again. raylib
 * statically links GLFW into this library so the symbols resolve, and glfwGetCurrentContext
 * returns raylib's own window without needing the GLFW headers.
 */
extern "C" void* glfwGetCurrentContext(void);
extern "C" void glfwSetWindowShouldClose(void* window, int value);

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
 * it into the size.
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

    /* Sampled as its own pixels rather than smoothed: see SampleAsPixels. */
    bool point = false;
};

/*
 * One picture to decode, or decoded: the path, the stamp the decode was asked for, and once it is
 * done the pixels, which are empty when raylib could not read the file.
 */
struct Decode
{
    std::string path;
    std::uintmax_t length = 0;
    std::filesystem::file_time_type written{};
    Image image{};
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
};

struct State
{
    bool initialised = false;
    bool windowOpen = false;
    ImGuiContext* context = nullptr;
    DeviewInput input{};

    /* Whether the last screen carried a context menu, which is what makes Escape and a click
     * outside it a dismissal rather than what they would otherwise mean. */
    bool menuOpen = false;

    /* What a wheel message left over. A notch is 1.0, and a touchpad sends fractions of one:
     * truncating each frame's value on its own threw all of them away, so a touchpad scrolled
     * nothing at all. */
    float scrollRemainder = 0.0f;

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
    };

    PictureSpace pictureSpaces[2];

    /*
     * An enlarged picture being dragged: where the button went down, and how the picture was placed
     * then, which the whole drag is measured from. Measured from the last frame instead, a drag
     * would drift by whatever each frame's clamp took off it.
     */
    bool panning = false;
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
};

State state;

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
 * How a picture's texture is sampled, set once as it is made.
 *
 * Bilinear, which is the whole of what a fitted picture needs: it is only ever drawn at its own
 * size or smaller. Clamped at its edges rather than repeating, which is raylib's default: sampled
 * at its last column, a repeating texture takes in its first, and a picture that is opaque on the
 * left and clear on the right grew a line of its left edge down its right.
 */
void PrepareTexture(CachedTexture& entry)
{
    SetTextureFilter(entry.texture, TEXTURE_FILTER_BILINEAR);
    SetTextureWrap(entry.texture, TEXTURE_WRAP_CLAMP);
    entry.point = false;
}

/*
 * A picture enlarged past its own size is drawn as the pixels it has, which is what zooming that
 * far in is for: smoothed, a one pixel difference between the two sides is a blur on both. Every
 * other picture is smoothed. Changed only when it has to be, since it is a texture parameter and
 * this is asked every frame.
 */
void SampleAsPixels(const std::string& path, bool point)
{
    const auto found = state.pictures.find(path);
    if (found == state.pictures.end() ||
        !found->second.loaded ||
        found->second.point == point)
    {
        return;
    }

    SetTextureFilter(found->second.texture, point ? TEXTURE_FILTER_POINT : TEXTURE_FILTER_BILINEAR);
    found->second.point = point;
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

        /* The file and stb_image under it, and nothing that touches GL. */
        decode.image = LoadImage(decode.path.c_str());

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
 */
void TakeDecoded()
{
    if (!state.decoder)
    {
        return;
    }

    std::vector<Decode> done;
    {
        const std::lock_guard<std::mutex> lock(state.decoder->mutex);
        done.swap(state.decoder->done);
    }

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
            if (decode.image.data != nullptr)
            {
                const Texture2D texture = LoadTextureFromImage(decode.image);
                if (IsTextureValid(texture))
                {
                    entry.texture = texture;
                    entry.loaded = true;
                    PrepareTexture(entry);
                }
            }
        }

        UnloadImage(decode.image);
    }
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
 * The decoded picture for a path, or null when there is none to draw: either this build cannot
 * read it, or it is still being decoded, which `loading` says so the pane can show that it is coming
 * rather than nothing. A capture decodes here and now, since it draws one frame and has no later one
 * for a decode to land in.
 *
 * Invalidated by the file's write time and length, which is the same freshness test the managed
 * queue poller uses: a re-run that rewrites a received image has to refresh the pane rather than
 * leave the previous one up.
 */
const Texture2D* Picture(const std::string& path, bool& loading)
{
    loading = false;
    if (path.empty())
    {
        return nullptr;
    }

    const std::filesystem::path file(path);
    std::error_code error;
    const auto written = std::filesystem::last_write_time(file, error);
    if (error)
    {
        ForgetPicture(path);
        return nullptr;
    }

    const auto length = std::filesystem::file_size(file, error);
    if (error)
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
            return found->second.loaded ? &found->second.texture : nullptr;
        }

        ForgetPicture(path);
    }

    CachedTexture entry;
    entry.written = written;
    entry.length = length;
    entry.used = true;
    if (state.capturing)
    {
        const Texture2D texture = LoadTexture(path.c_str());
        if (IsTextureValid(texture))
        {
            entry.texture = texture;
            entry.loaded = true;
            PrepareTexture(entry);
        }
    }
    else
    {
        entry.decoding = true;
        loading = true;
        RequestDecode(path, length, written);
    }

    const auto inserted = state.pictures.emplace(path, entry).first;
    return inserted->second.loaded ? &inserted->second.texture : nullptr;
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

void PumpInput()
{
    ImGuiIO& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(static_cast<float>(GetScreenWidth()), static_cast<float>(GetScreenHeight()));
    io.DeltaTime = GetFrameTime() > 0.0f ? GetFrameTime() : 1.0f / 60.0f;

    const Vector2 mouse = GetMousePosition();
    io.AddMousePosEvent(mouse.x, mouse.y);
    io.AddMouseButtonEvent(ImGuiMouseButton_Left, IsMouseButtonDown(MOUSE_BUTTON_LEFT));
    io.AddMouseButtonEvent(ImGuiMouseButton_Right, IsMouseButtonDown(MOUSE_BUTTON_RIGHT));
    io.AddMouseButtonEvent(ImGuiMouseButton_Middle, IsMouseButtonDown(MOUSE_BUTTON_MIDDLE));

    const Vector2 wheel = GetMouseWheelMoveV();
    io.AddMouseWheelEvent(wheel.x, wheel.y);
}

int ReadKey()
{
    /* Super as well as control, so a macOS keyboard driving the Linux build through a remote
     * session still copies with the chord its user has in their fingers. */
    const bool control =
        IsKeyDown(KEY_LEFT_CONTROL) || IsKeyDown(KEY_RIGHT_CONTROL) ||
        IsKeyDown(KEY_LEFT_SUPER) || IsKeyDown(KEY_RIGHT_SUPER);
    if (control)
    {
        /* Answered before the unmodified keys below, and returning none for anything else: without
         * this ctrl+a fell through to plain A, which accepts. */
        if (IsKeyPressed(KEY_C)) return DEVIEW_KEY_COPY;
        if (IsKeyPressed(KEY_A)) return DEVIEW_KEY_SELECT_ALL;
        /* With control as well as without, since that is the chord everything else that zooms
         * taught. By position here: a character is not reported while control is held. */
        if (IsKeyPressed(KEY_EQUAL) || IsKeyPressed(KEY_KP_ADD)) return DEVIEW_KEY_ZOOM_IN;
        if (IsKeyPressed(KEY_MINUS) || IsKeyPressed(KEY_KP_SUBTRACT)) return DEVIEW_KEY_ZOOM_OUT;
        if (IsKeyPressed(KEY_ZERO) || IsKeyPressed(KEY_KP_0)) return DEVIEW_KEY_ZOOM_RESET;
        return DEVIEW_KEY_NONE;
    }

    /* The key itself held down, rather than read off the case of what was typed: see below. */
    const bool shift = IsKeyDown(KEY_LEFT_SHIFT) || IsKeyDown(KEY_RIGHT_SHIFT);

    /* Letters by the character typed rather than by key position. raylib's key codes are
     * positions on a US layout, so on AZERTY the key labelled Q reported KEY_A and accepted - a
     * snapshot written into source by a key meant to quit - while the one labelled A quit.
     * Characters follow the layout, the way the macOS and Windows heads already do. */
    for (int character = GetCharPressed(); character != 0; character = GetCharPressed())
    {
        /* Which letter, and nothing of its case. A capital says that Shift or Caps Lock was on and
         * not which of them, so read as typed Caps Lock turned a plain A into accept all - every
         * pending snapshot written into source, with nothing asked first, by the key that accepts
         * one - and left D, V, Q, N, P, M, R and J doing nothing. */
        if (character >= 'A' && character <= 'Z')
        {
            character += 'a' - 'A';
        }

        switch (character)
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

    if (IsKeyPressed(KEY_UP)) return DEVIEW_KEY_SCROLL_UP;
    if (IsKeyPressed(KEY_DOWN)) return DEVIEW_KEY_SCROLL_DOWN;
    if (IsKeyPressed(KEY_PAGE_UP)) return DEVIEW_KEY_PAGE_UP;
    if (IsKeyPressed(KEY_PAGE_DOWN)) return DEVIEW_KEY_PAGE_DOWN;
    if (IsKeyPressed(KEY_HOME)) return DEVIEW_KEY_HOME;
    if (IsKeyPressed(KEY_END)) return DEVIEW_KEY_END;
    if (IsKeyPressed(KEY_TAB)) return shift ? DEVIEW_KEY_PREVIOUS_ITEM : DEVIEW_KEY_NEXT_ITEM;
    if (IsKeyPressed(KEY_ESCAPE)) return DEVIEW_KEY_QUIT;
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
    ImGui::SetCurrentContext(state.context);
    const float width = ImGui::CalcTextSize("M").x;
    const float height = ImGui::GetTextLineHeightWithSpacing();
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

        list->AddText(
            ImVec2(textPos.x + static_cast<float>(segment.column) * cell, textPos.y),
            colour,
            begin,
            end);
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
            leftHit.textLeft < 0.0f ||
            mouse.x < leftHit.cellLeft ||
            /* The splitter's grab zone overlaps the left pane's edge, and a drag that started
             * there would otherwise also select whatever it began over. */
            (dividerX >= 0.0f && mouse.x <= dividerX + grabWidth))
        {
            return;
        }

        const bool right = rightHit.cellLeft >= 0.0f && mouse.x >= rightHit.cellLeft;
        if (right && rightHit.textLeft < 0.0f)
        {
            return;
        }

        const PaneHit& hit = right ? rightHit : leftHit;
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

void DrawChecker(ImDrawList* list, const ImVec2& min, const ImVec2& max)
{
    list->AddRectFilled(min, max, IM_COL32(64, 64, 64, 255));
    const ImU32 dark = IM_COL32(48, 48, 48, 255);
    int row = 0;
    for (float y = min.y; y < max.y; y += checkerSize, row++)
    {
        int column = 0;
        for (float x = min.x; x < max.x; x += checkerSize, column++)
        {
            if ((row & 1) == (column & 1))
            {
                continue;
            }

            list->AddRectFilled(
                ImVec2(x, y),
                ImVec2(std::min(x + checkerSize, max.x), std::min(y + checkerSize, max.y)),
                dark);
        }
    }
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
    const Texture2D* texture = Picture(path, loading);
    if (texture == nullptr)
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
    SampleAsPixels(
        path,
        pane.imageZoom > 1.0f &&
        fitted.x * pane.imageZoom >= static_cast<float>(texture->width));
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

    DrawChecker(list, min, max);
    list->AddImage(static_cast<ImTextureID>(texture->id), min, max, uvMin, uvMax);
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

        for (const State::PictureSpace& space : state.pictureSpaces)
        {
            if (space.present &&
                space.enlarged &&
                mouse.x >= space.left &&
                mouse.x < space.left + space.width &&
                mouse.y >= space.top &&
                mouse.y < space.top + space.height)
            {
                state.panning = true;
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

    /* The picture follows the pointer, so the point at the middle moves the other way. */
    const State::PictureSpace& from = state.panFrom;
    const float x = from.centreX - (mouse.x - state.panStart.x) / from.wholeWidth;
    const float y = from.centreY - (mouse.y - state.panStart.y) / from.wholeHeight;
    state.input.panX = std::min(std::max(x, from.across * 0.5f), 1.0f - from.across * 0.5f);
    state.input.panY = std::min(std::max(y, from.down * 0.5f), 1.0f - from.down * 0.5f);

    if (!ImGui::IsMouseDown(ImGuiMouseButton_Left))
    {
        state.panning = false;
    }

    return true;
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

    Text(screen, screen->titleOffset, screen->titleLength);
    const std::string subtitle = Copy(screen, screen->subtitleOffset, screen->subtitleLength);
    if (!subtitle.empty())
    {
        const float width = ImGui::CalcTextSize(subtitle.c_str()).x;
        ImGui::SameLine(ImGui::GetContentRegionAvail().x - width);
        ImGui::TextDisabled("%s", subtitle.c_str());
    }

    ImGui::Separator();

    const float footer = ImGui::GetFrameHeightWithSpacing() + ImGui::GetStyle().ItemSpacing.y;

    /*
     * The strip the pane scrollbar gets, taken off the body before anything is laid out in it.
     * Always reserved rather than appearing once a document outgrows the window: a strip that came
     * and went would shift the pane split every time the selection changed.
     */
    const float scrollbarWidth = ImGui::GetStyle().ScrollbarSize;
    ImGui::BeginChild("##body", ImVec2(-scrollbarWidth, -footer), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar);

    /* Read back rather than recomputed, so the scrollbar lands against the body whatever the
     * negative sizes above worked out as. */
    const ImVec2 bodyOrigin = ImGui::GetWindowPos();
    const ImVec2 bodyExtent = ImGui::GetWindowSize();

    const bool hasQueue = screen->queueCount > 0;
    const int columns = hasQueue ? 3 : 2;
    const float cell = ImGui::CalcTextSize("M").x;
    ImVec2 menuAnchor;
    bool menuAnchored = false;
    if (state.queueWidth <= 0.0f)
    {
        state.queueWidth = cell * queueCells;
    }

    /* The body, measured before the table so the drag zone can span all of it rather than only the
     * rows the table happens to have. */
    const ImVec2 bodyMin = ImGui::GetCursorScreenPos();
    const ImVec2 bodyAvail = ImGui::GetContentRegionAvail();
    const float queueWidth = ClampQueueWidth(state.queueWidth, bodyAvail.x, cell);

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

        ImGui::TableSetupColumn(Copy(screen, left.headerOffset, left.headerLength).c_str());
        ImGui::TableSetupColumn(Copy(screen, right.headerOffset, right.headerLength).c_str());
        ImGui::TableHeadersRow();

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
                        if (ImGui::Selectable(label.c_str(), false))
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
                        if (ImGui::Selectable(label.c_str(), selected))
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

                    if (index == screen->menuRow &&
                        screen->menuCount > 0)
                    {
                        menuAnchor = ImVec2(ImGui::GetItemRectMin().x, ImGui::GetItemRectMax().y);
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
        ImGui::SetCursorScreenPos(ImVec2(dividerX - grabWidth, bodyMin.y));
        ImGui::InvisibleButton(
            "##queue-splitter",
            ImVec2(grabWidth * 2.0f + 1.0f, std::max(1.0f, bodyAvail.y)));
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
            /* Kept inside the window: a click near its right or bottom edge would otherwise hang
             * most of the menu off it. */
            const ImVec2 display = ImGui::GetIO().DisplaySize;
            position.x = std::max(0.0f, std::min(position.x, display.x - size.x));
            position.y = std::max(0.0f, std::min(position.y, display.y - size.y));
        }

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
            if (ImGui::Selectable(labels[static_cast<size_t>(index)].c_str()))
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
        const std::string label = Copy(screen, button.labelOffset, button.labelLength);
        const bool enabled = (button.flags & DEVIEW_BUTTON_ENABLED) != 0;
        if (index > 0)
        {
            ImGui::SameLine();
        }

        if (!enabled)
        {
            ImGui::BeginDisabled();
        }

        ImGui::PushID(index);
        if (ImGui::Button(label.c_str()))
        {
            state.input.clickedButton = index;
        }

        ImGui::PopID();
        if (!enabled)
        {
            ImGui::EndDisabled();
        }
    }

    const std::string status = Copy(screen, screen->statusOffset, screen->statusLength);
    if (!status.empty())
    {
        const float width = ImGui::CalcTextSize(status.c_str()).x;
        ImGui::SameLine();
        const float available = ImGui::GetContentRegionAvail().x;
        if (available > width)
        {
            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + available - width);
        }

        ImGui::TextDisabled("%s", status.c_str());
    }

    ImGui::End();
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

    state.tracked = false;
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
    SetTargetFPS(60);

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

    if (fontTtf != nullptr && fontLength > 0)
    {
        /* ImGui frees font data with its own allocator, so hand it a copy rather than memory
         * owned by the managed heap. */
        void* copy = IM_ALLOC(static_cast<size_t>(fontLength));
        memcpy(copy, fontTtf, static_cast<size_t>(fontLength));
        ImFontConfig config;
        config.FontDataOwnedByAtlas = true;
        config.ExtraSizeScale = emScale;
        io.Fonts->AddFontFromMemoryTTF(copy, fontLength, fontSize <= 0.0f ? 15.0f : fontSize, &config);
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
    PumpInput();
    /* Before the frame asks for its pictures, so one that finished decoding since the last frame is
     * drawn in this one. */
    TakeDecoded();
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

    BeginDrawing();
    ClearBackground(Color{24, 24, 24, 255});
    RenderDrawData(ImGui::GetDrawData());
    EndDrawing();
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
        state.input.key = ReadKey();

        /* Escape with a menu up dismisses the menu. It reached the managed side as quit, which
         * closes the menu and then runs the command, so Esc-to-dismiss closed the viewer - and on
         * Linux there is no tray to open it again from, so the queue went to staging. */
        if (state.menuOpen &&
            state.input.key == DEVIEW_KEY_QUIT &&
            IsKeyPressed(KEY_ESCAPE))
        {
            state.input.key = DEVIEW_KEY_NONE;
            state.input.menuClosed = 1;
        }

        /* Whole notches, keeping the fraction. A touchpad sends a fraction of one per frame and
         * truncating each frame on its own threw every one of them away. */
        const Vector2 wheel = GetMouseWheelMoveV();
        state.scrollRemainder += wheel.y;
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

    if (state.context != nullptr)
    {
        ImGui::SetCurrentContext(state.context);
        ImGui::DestroyContext(state.context);
        state.context = nullptr;
    }

    CloseWindow();
    state.initialised = false;
    state.windowOpen = false;
}
}
