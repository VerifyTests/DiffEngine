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
#include <cstring>
#include <string>
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

struct State
{
    bool initialised = false;
    bool windowOpen = false;
    ImGuiContext* context = nullptr;
    DeviewInput input{};

    /*
     * The queue column, owned here rather than by the table.
     *
     * ImGuiTableFlags_Resizable would give the drag for free, but it also hands the width to
     * ImGui's own table state, which initialises once and then auto-fits or restores from saved
     * settings. A column that is fixed and not resizable takes InitStretchWeightOrWidth on every
     * frame instead, which is a width this side decides and can therefore reproduce: the pixel
     * captures share one context and one table id and draw a single frame each, so anything
     * carried between them shows up as a snapshot that depends on the test order.
     */
    float queueWidth = 0.0f;

    /* What was last handed to raylib, so an idle frame is not a window system call. */
    int cursor = MOUSE_CURSOR_DEFAULT;
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

/* Spaces only. A queue label's indent is the grouping drawn as layout, and a tip is not laid out. */
std::string Trim(const std::string& value)
{
    const size_t first = value.find_first_not_of(' ');
    if (first == std::string::npos)
    {
        return {};
    }

    return value.substr(first, value.find_last_not_of(' ') - first + 1);
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
            const ImDrawVert& vertex = vertices[indices[indexStart + index + corner]];
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

void DrawRow(const DeviewScreen* screen, const DeviewPane& pane, int index, int column)
{
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
    ImGui::PushStyleColor(ImGuiCol_Text, RowColour(row.kind));
    Text(screen, row.textOffset, row.textLength);
    ImGui::PopStyleColor();
}

void BuildFrame(const DeviewScreen* screen)
{
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
                    const std::string label = Copy(screen, item.labelOffset, item.labelLength);
                    if (item.flags & DEVIEW_QUEUE_HEADER)
                    {
                        /* A heading, not a row: dimmed like the subtitle, and plain text rather
                         * than a Selectable so it neither hovers nor left-clicks. Right-clicks
                         * still count: a heading's menu is how a whole group is swept. */
                        ImGui::TextDisabled("%s", label.c_str());
                    }
                    else
                    {
                        const bool selected = (item.flags & DEVIEW_QUEUE_SELECTED) != 0;
                        if (item.flags & DEVIEW_QUEUE_FAILED)
                        {
                            ImGui::PushStyleColor(ImGuiCol_Text, RowColour(DEVIEW_ROW_REMOVED));
                        }

                        ImGui::PushID(index);
                        if (ImGui::Selectable(label.c_str(), selected))
                        {
                            state.input.clickedQueueItem = index;
                        }

                        ImGui::PopID();
                        if (item.flags & DEVIEW_QUEUE_FAILED)
                        {
                            ImGui::PopStyleColor();
                        }
                    }

                    if (ImGui::IsItemClicked(ImGuiMouseButton_Right))
                    {
                        state.input.rightClickedQueueItem = index;
                    }

                    /* The full name and the failure behind the `!`, which is the only place either
                     * is readable: the column clips a long label, and the status text has nowhere
                     * else to go. Matches the WinForms head's tip, indent trimmed because that is
                     * layout, conflict marker kept because it means something. */
                    if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayNormal))
                    {
                        std::string tip = Trim(label);
                        const std::string status = Copy(screen, item.statusOffset, item.statusLength);
                        if (!status.empty())
                        {
                            tip += "\n" + status;
                        }

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

            DrawRow(screen, left, index, column);
            ImGui::TableSetColumnIndex(column + 1);
            DrawRow(screen, right, index, column + 1);
        }

        ImGui::EndTable();
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

        ImGui::End();
        ImGui::PopStyleVar();
        ImGui::PopStyleColor();
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
    unsigned int flags = FLAG_WINDOW_RESIZABLE;
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
        const Vector2 wheel = GetMouseWheelMoveV();
        state.input.scrollDelta = static_cast<int32_t>(wheel.y);
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

    ImGui::SetCurrentContext(state.context);
    ImGuiIO& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(static_cast<float>(width), static_cast<float>(height));
    io.DeltaTime = 1.0f / 60.0f;

    RenderTexture2D target = LoadRenderTexture(width, height);
    if (!IsRenderTextureValid(target))
    {
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

void deview_focus(void)
{
    if (!state.initialised)
    {
        return;
    }

    ClearWindowState(FLAG_WINDOW_HIDDEN);
    SetWindowFocused();
}

void deview_shutdown(void)
{
    if (!state.initialised)
    {
        return;
    }

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
