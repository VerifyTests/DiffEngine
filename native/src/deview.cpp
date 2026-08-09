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
#include "raylib.h"
#include "rlgl.h"

#include <cstring>
#include <string>

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

struct State
{
    bool initialised = false;
    bool windowOpen = false;
    ImGuiContext* context = nullptr;
    DeviewInput input{};
};

State state;

void ResetInput()
{
    state.input.key = DEVIEW_KEY_NONE;
    state.input.clickedButton = -1;
    state.input.clickedQueueItem = -1;
    state.input.scrollDelta = 0;
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
    ImGui::BeginChild("##body", ImVec2(0, -footer), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar);

    const bool hasQueue = screen->queueCount > 0;
    const int columns = hasQueue ? 3 : 2;
    if (screen->paneCount >= 2 &&
        ImGui::BeginTable("##panes", columns, ImGuiTableFlags_BordersInnerV | ImGuiTableFlags_SizingStretchSame))
    {
        const DeviewPane& left = screen->panes[0];
        const DeviewPane& right = screen->panes[1];
        if (hasQueue)
        {
            ImGui::TableSetupColumn("Pending", ImGuiTableColumnFlags_WidthFixed, 220.0f);
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
            }

            ImGui::TableSetColumnIndex(column);
            DrawRow(screen, left, index, column);
            ImGui::TableSetColumnIndex(column + 1);
            DrawRow(screen, right, index, column + 1);
        }

        ImGui::EndTable();
    }

    ImGui::EndChild();
    ImGui::Separator();

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
