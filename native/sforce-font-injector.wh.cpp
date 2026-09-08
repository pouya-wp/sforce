// ==WindhawkMod==
// @id              sforce-font-injector
// @name            SFORCE Font Injector
// @description     Forces SF Pro and SF Arabic through DirectWrite and GDI while preserving icons and code fonts.
// @version         2.0.0
// @author          SFORCE
// @include         explorer.exe
// @include         ShellExperienceHost.exe
// @include         StartMenuExperienceHost.exe
// @include         SearchHost.exe
// @include         SystemSettings.exe
// @include         ApplicationFrameHost.exe
// @include         Notion.exe
// @include         Claude.exe
// @include         ChatGPT.exe
// @include         msedge.exe
// @include         chrome.exe
// @include         brave.exe
// @include         Arc.exe
// @include         firefox.exe
// @include         Code.exe
// @include         Cursor.exe
// @architecture    x86-64
// @compilerOptions -ldwrite -lgdi32
// @license         GPL-3.0
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
# SFORCE Font Injector

Applies SF Pro to normal DirectWrite/GDI text and SF Arabic to Arabic-script
ranges. Icon, emoji, private-use, and common monospace families are left alone.
The process allow-list intentionally avoids security-critical Windows processes.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- latinFamily: SF Pro
  $name: Latin family
- arabicFamily: SF Arabic
  $name: Arabic family
- minimumWeight: 500
  $name: Minimum font weight
- preserveMonospace: true
  $name: Preserve code fonts
*/
// ==/WindhawkModSettings==

#include <windows.h>
#include <dwrite.h>
#include <windhawk_api.h>

#include <algorithm>
#include <cwctype>
#include <string>

namespace {

std::wstring g_latinFamily = L"SF Pro";
std::wstring g_arabicFamily = L"SF Arabic";
DWRITE_FONT_WEIGHT g_minimumWeight = DWRITE_FONT_WEIGHT_MEDIUM;
bool g_preserveMonospace = true;

using CreateTextLayout_t = HRESULT(STDMETHODCALLTYPE*)(
    IDWriteFactory*, WCHAR const*, UINT32, IDWriteTextFormat*, FLOAT, FLOAT,
    IDWriteTextLayout**);
using CreateGdiCompatibleTextLayout_t = HRESULT(STDMETHODCALLTYPE*)(
    IDWriteFactory*, WCHAR const*, UINT32, IDWriteTextFormat*, FLOAT, FLOAT,
    FLOAT, DWRITE_MATRIX const*, BOOL, IDWriteTextLayout**);

CreateTextLayout_t g_createTextLayoutOriginal = nullptr;
CreateGdiCompatibleTextLayout_t g_createGdiTextLayoutOriginal = nullptr;

using CreateFontIndirectW_t = HFONT(WINAPI*)(const LOGFONTW*);
CreateFontIndirectW_t g_createFontIndirectWOriginal = nullptr;

std::wstring ToUpper(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(),
                   [](wchar_t c) { return static_cast<wchar_t>(std::towupper(c)); });
    return value;
}

bool ContainsAny(const std::wstring& text,
                 std::initializer_list<const wchar_t*> needles) {
    for (const auto* needle : needles) {
        if (text.find(needle) != std::wstring::npos) {
            return true;
        }
    }
    return false;
}

bool PreserveFamily(const wchar_t* family) {
    if (!family || !*family) {
        return false;
    }

    const auto upper = ToUpper(family);
    if (ContainsAny(upper, {L"ICON", L"SYMBOL", L"GLYPH", L"WINGDINGS",
                            L"WEBDINGS", L"MARLETT", L"MDL2", L"FLUENT",
                            L"EMOJI", L"EMOTICON"})) {
        return true;
    }

    return g_preserveMonospace &&
           ContainsAny(upper, {L"CONSOLAS", L"CASCADIA", L"SF MONO",
                               L"MONO", L"CODE", L"TERMINAL"});
}

std::wstring GetFormatFamily(IDWriteTextFormat* format) {
    const UINT32 length = format->GetFontFamilyNameLength();
    std::wstring family(length + 1, L'\0');
    if (FAILED(format->GetFontFamilyName(family.data(), length + 1))) {
        return {};
    }
    family.resize(length);
    return family;
}

enum class Script : BYTE { Latin, Arabic, Preserve };

Script ClassifyUtf16(const WCHAR* text, UINT32 length, UINT32 index) {
    const UINT32 ch = text[index];
    if ((ch >= 0x0600 && ch <= 0x06FF) ||
        (ch >= 0x0750 && ch <= 0x077F) ||
        (ch >= 0x0870 && ch <= 0x089F) ||
        (ch >= 0x08A0 && ch <= 0x08FF) ||
        (ch >= 0xFB50 && ch <= 0xFDFF) ||
        (ch >= 0xFE70 && ch <= 0xFEFF)) {
        return Script::Arabic;
    }

    if ((ch >= 0xE000 && ch <= 0xF8FF) ||
        (ch >= 0xD800 && ch <= 0xDFFF) ||
        (ch >= 0xFE00 && ch <= 0xFE0F)) {
        return Script::Preserve;
    }

    return Script::Latin;
}

void ApplyRun(IDWriteTextLayout* layout, IDWriteFontCollection* collection,
              Script script, UINT32 start, UINT32 length,
              DWRITE_FONT_WEIGHT weight) {
    if (length == 0 || script == Script::Preserve) {
        return;
    }

    const DWRITE_TEXT_RANGE range{start, length};
    layout->SetFontCollection(collection, range);
    layout->SetFontFamilyName(
        script == Script::Arabic ? g_arabicFamily.c_str() : g_latinFamily.c_str(),
        range);
    layout->SetFontWeight(weight, range);
}

void ForceLayout(IDWriteFactory* factory, const WCHAR* text, UINT32 textLength,
                 IDWriteTextFormat* format, IDWriteTextLayout* layout) {
    if (!text || textLength == 0 || !format || !layout) {
        return;
    }

    const auto sourceFamily = GetFormatFamily(format);
    if (PreserveFamily(sourceFamily.c_str())) {
        return;
    }

    IDWriteFontCollection* collection = nullptr;
    if (FAILED(factory->GetSystemFontCollection(&collection, FALSE)) || !collection) {
        return;
    }

    const auto requestedWeight = static_cast<int>(format->GetFontWeight());
    const auto weight = static_cast<DWRITE_FONT_WEIGHT>(
        std::max(requestedWeight, static_cast<int>(g_minimumWeight)));

    UINT32 runStart = 0;
    Script script = ClassifyUtf16(text, textLength, 0);
    for (UINT32 i = 1; i <= textLength; ++i) {
        const Script next = i < textLength ? ClassifyUtf16(text, textLength, i)
                                           : Script::Preserve;
        if (i == textLength || next != script) {
            ApplyRun(layout, collection, script, runStart, i - runStart, weight);
            runStart = i;
            script = next;
        }
    }

    collection->Release();
}

HRESULT STDMETHODCALLTYPE CreateTextLayoutHook(
    IDWriteFactory* factory, WCHAR const* text, UINT32 textLength,
    IDWriteTextFormat* format, FLOAT maxWidth, FLOAT maxHeight,
    IDWriteTextLayout** layout) {
    const HRESULT result = g_createTextLayoutOriginal(
        factory, text, textLength, format, maxWidth, maxHeight, layout);
    if (SUCCEEDED(result) && layout && *layout) {
        ForceLayout(factory, text, textLength, format, *layout);
    }
    return result;
}

HRESULT STDMETHODCALLTYPE CreateGdiCompatibleTextLayoutHook(
    IDWriteFactory* factory, WCHAR const* text, UINT32 textLength,
    IDWriteTextFormat* format, FLOAT width, FLOAT height, FLOAT pixelsPerDip,
    DWRITE_MATRIX const* transform, BOOL useGdiNatural,
    IDWriteTextLayout** layout) {
    const HRESULT result = g_createGdiTextLayoutOriginal(
        factory, text, textLength, format, width, height, pixelsPerDip,
        transform, useGdiNatural, layout);
    if (SUCCEEDED(result) && layout && *layout) {
        ForceLayout(factory, text, textLength, format, *layout);
    }
    return result;
}

HFONT WINAPI CreateFontIndirectWHook(const LOGFONTW* source) {
    if (!source || PreserveFamily(source->lfFaceName)) {
        return g_createFontIndirectWOriginal(source);
    }

    LOGFONTW font = *source;
    wcsncpy_s(font.lfFaceName, g_latinFamily.c_str(), LF_FACESIZE - 1);
    font.lfWeight = std::max(font.lfWeight, static_cast<LONG>(g_minimumWeight));
    return g_createFontIndirectWOriginal(&font);
}

void LoadSettings() {
    if (PCWSTR value = Wh_GetStringSetting(L"latinFamily")) {
        if (*value) g_latinFamily = value;
        Wh_FreeStringSetting(value);
    }
    if (PCWSTR value = Wh_GetStringSetting(L"arabicFamily")) {
        if (*value) g_arabicFamily = value;
        Wh_FreeStringSetting(value);
    }
    g_minimumWeight = static_cast<DWRITE_FONT_WEIGHT>(
        std::clamp(Wh_GetIntSetting(L"minimumWeight"), 100, 900));
    g_preserveMonospace = Wh_GetIntSetting(L"preserveMonospace") != 0;
}

bool HookDirectWriteFactory() {
    IDWriteFactory* factory = nullptr;
    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,
                                   __uuidof(IDWriteFactory),
                                   reinterpret_cast<IUnknown**>(&factory))) ||
        !factory) {
        return false;
    }

    void** vtable = *reinterpret_cast<void***>(factory);
    const BOOL layoutHooked = Wh_SetFunctionHook(
        vtable[18], reinterpret_cast<void*>(CreateTextLayoutHook),
        reinterpret_cast<void**>(&g_createTextLayoutOriginal));
    const BOOL gdiLayoutHooked = Wh_SetFunctionHook(
        vtable[19], reinterpret_cast<void*>(CreateGdiCompatibleTextLayoutHook),
        reinterpret_cast<void**>(&g_createGdiTextLayoutOriginal));
    factory->Release();
    return layoutHooked && gdiLayoutHooked;
}

}  // namespace

BOOL Wh_ModInit() {
    LoadSettings();

    const bool directWriteHooked = HookDirectWriteFactory();
    const bool gdiHooked = Wh_SetFunctionHook(
        reinterpret_cast<void*>(CreateFontIndirectW),
        reinterpret_cast<void*>(CreateFontIndirectWHook),
        reinterpret_cast<void**>(&g_createFontIndirectWOriginal));

    Wh_Log(L"SFORCE initialized: DirectWrite=%d GDI=%d", directWriteHooked,
           gdiHooked);
    return directWriteHooked || gdiHooked;
}

void Wh_ModSettingsChanged() {
    LoadSettings();
}
