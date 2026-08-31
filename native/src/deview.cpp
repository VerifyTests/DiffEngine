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
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <map>
#include <string>
#include <system_error>
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
 * A false `loaded` is a remembered failure. raylib is built here with decoders for PNG, JPEG, BMP
 * and GIF and has none for WebP or ICO, so a pane can legitimately carry a path this build cannot
 * read; remembering that means attempting it once rather than once a frame. Nothing is lost when it
 * happens — the rows already say what the file is, and they are the description an image comparison
 * is made of.
 */
struct CachedTexture
{
    Texture2D texture{};
    bool loaded = false;
    std::uintmax_t length = 0;
    std::filesystem::file_time_type written{};
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
};

State state;

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

    state.pictures.erase(found);
}

/*
 * The decoded picture for a path, or null when this build cannot read it.
 *
 * Invalidated by the file's write time and length, which is the same freshness test the managed
 * queue poller uses: a re-run that rewrites a received image has to refresh the pane rather than
 * leave the previous one up.
 */
const Texture2D* Picture(const std::string& path)
{
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
        if (found->second.written == written &&
            found->second.length == length)
        {
            return found->second.loaded ? &found->second.texture : nullptr;
        }

        ForgetPicture(path);
    }

    CachedTexture entry;
    entry.written = written;
    entry.length = length;
    const Texture2D texture = LoadTexture(path.c_str());
    if (IsTextureValid(texture))
    {
        entry.texture = texture;
        entry.loaded = true;
        /* A picture is only ever scaled down here, so bilinear is the whole of what the filter has
         * to do. */
        SetTextureFilter(entry.texture, TEXTURE_FILTER_BILINEAR);
    }

    const auto inserted = state.pictures.emplace(path, entry).first;
    return inserted->second.loaded ? &inserted->second.texture : nullptr;
}

void UnloadPictures()
{
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

    const float height = static_cast<float>(GetScreenHeight());
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
        return DEVIEW_KEY_NONE;
    }

    if (IsKeyPressed(KEY_UP)) return DEVIEW_KEY_SCROLL_UP;
    if (IsKeyPressed(KEY_DOWN)) return DEVIEW_KEY_SCROLL_DOWN;
    if (IsKeyPressed(KEY_PAGE_UP)) return DEVIEW_KEY_PAGE_UP;
    if (IsKeyPressed(KEY_PAGE_DOWN)) return DEVIEW_KEY_PAGE_DOWN;
    if (IsKeyPressed(KEY_HOME)) return DEVIEW_KEY_HOME;
    if (IsKeyPressed(KEY_END)) return DEVIEW_KEY_END;
    if (IsKeyPressed(KEY_N)) return DEVIEW_KEY_NEXT_CHANGE;
    if (IsKeyPressed(KEY_P)) return DEVIEW_KEY_PREVIOUS_CHANGE;
    if (IsKeyPressed(KEY_TAB)) return IsKeyDown(KEY_LEFT_SHIFT) || IsKeyDown(KEY_RIGHT_SHIFT)
        ? DEVIEW_KEY_PREVIOUS_ITEM
        : DEVIEW_KEY_NEXT_ITEM;
    if (IsKeyPressed(KEY_A)) return IsKeyDown(KEY_LEFT_SHIFT) || IsKeyDown(KEY_RIGHT_SHIFT)
        ? DEVIEW_KEY_ACCEPT_ALL
        : DEVIEW_KEY_ACCEPT;
    if (IsKeyPressed(KEY_D)) return DEVIEW_KEY_DISCARD;
    if (IsKeyPressed(KEY_V)) return DEVIEW_KEY_NEXT_VARIANT;
    if (IsKeyPressed(KEY_Q) || IsKeyPressed(KEY_ESCAPE)) return DEVIEW_KEY_QUIT;
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

    /* Where the row text starts, past that gutter, read from the first row that draws any. Stays
     * -1 for a pane of nothing but filler, which has nothing to select either. */
    float textLeft = -1.0f;

    /* The top of row zero and the pitch between rows, read from the first two rows the way
     * PaneImage reads them. */
    float first = -1.0f;
    float pitch = 0.0f;
};

void DrawRow(const DeviewScreen* screen, const DeviewPane& pane, int index, int column, PaneHit& hit)
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
        ImGui::Text("%c %4d", RowMarker(row.kind), row.lineNumber);
    }
    else
    {
        ImGui::Text("%c     ", RowMarker(row.kind));
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
    Text(screen, row.textOffset, row.textLength);
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

/* Called from inside the cell, which is the only place these are knowable. */
void RecordPaneImage(PaneImage& bounds, const DeviewPane& pane, int index)
{
    if (pane.imagePathLength <= 0 ||
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
 * The picture under a pane's rows. Absolutely positioned over the table rather than submitted as a
 * table row, because the rows a pane has and the rows the table has are different numbers: the
 * queue column is usually the tallest, and the space this fills is the pane's share of what the
 * queue is using.
 */
void DrawPaneImage(const DeviewScreen* screen, const DeviewPane& pane, const PaneImage& bounds, float bottom)
{
    if (pane.imagePathLength <= 0 ||
        pane.imageWidth <= 0 ||
        pane.imageHeight <= 0 ||
        bounds.width <= 0.0f ||
        bounds.first < 0.0f ||
        bounds.pitch <= 0.0f)
    {
        return;
    }

    const float top = bounds.first + static_cast<float>(pane.rowCount + 1) * bounds.pitch;
    const float available = bottom - top;
    if (available <= 0.0f)
    {
        return;
    }

    const Texture2D* texture = Picture(Copy(screen, pane.imagePathOffset, pane.imagePathLength));
    if (texture == nullptr)
    {
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
    const ImVec2 size(
        std::max(1.0f, static_cast<float>(pane.imageWidth) * scale),
        std::max(1.0f, static_cast<float>(pane.imageHeight) * scale));
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

    ImDrawList* list = ImGui::GetWindowDrawList();
    DrawChecker(list, min, max);
    list->AddImage(static_cast<ImTextureID>(texture->id), min, max);
    /* An outline, so a picture whose edges are the colour of the pane still has visible extent. */
    list->AddRect(
        ImVec2(min.x - 1.0f, min.y - 1.0f),
        ImVec2(max.x + 1.0f, max.y + 1.0f),
        IM_COL32(70, 70, 70, 255));
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
            ImGui::TableSetupColumn("Pending", ImGuiTableColumnFlags_WidthFixed, queueWidth);
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
            DrawRow(screen, left, index, column, leftHit);
            ImGui::TableSetColumnIndex(column + 1);
            RecordPaneImage(rightImage, right, index);
            DrawRow(screen, right, index, column + 1, rightHit);
        }

        ImGui::EndTable();
    }

    if (screen->paneCount >= 2)
    {
        const float bottom = bodyMin.y + bodyAvail.y;
        DrawPaneImage(screen, screen->panes[0], leftImage, bottom);
        DrawPaneImage(screen, screen->panes[1], rightImage, bottom);
    }

    /* After the table, which is where the geometry it reads becomes complete, and before the
     * splitter, which claims its own clicks. */
    UpdateSelection(screen, leftHit, rightHit, bodyMin, bodyAvail, dividerX, cell);

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
    if (screen->menuCount > 0 && menuAnchored)
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
        ImGui::SetNextWindowPos(ImVec2(menuAnchor.x + cell, menuAnchor.y));
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

    SetConfigFlags(flags);
    InitWindow(width, height, title == nullptr ? "DiffEngineViewer" : title);
    if (!IsWindowReady())
    {
        return 0;
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
        state.input.scrollDelta = notches;
        state.scrollRemainder -= static_cast<float>(notches);
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
    BuildFrame(screen);
    ImGui::Render();

    BeginTextureMode(target);
    ClearBackground(Color{24, 24, 24, 255});
    RenderDrawData(ImGui::GetDrawData());
    EndTextureMode();

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
