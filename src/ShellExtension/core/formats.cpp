#include "core/formats.h"

#include "core/text.h"

namespace qic {

const std::vector<FormatInfo>& AllFormats() {
    // Order matters: it is the order of SupportedInputExtensions() and of the MSIX ItemTypes.
    static const std::vector<FormatInfo> formats = {
        {"png", L"PNG", true, false, {L".png"}},
        {"jpg", L"JPG", true, true, {L".jpg", L".jpeg", L".jpe", L".jfif"}},
        {"bmp", L"BMP", true, false, {L".bmp", L".dib"}},
        {"tiff", L"TIFF", true, false, {L".tif", L".tiff"}},
        {"webp", L"WEBP", true, false, {L".webp"}},
        {"avif", L"AVIF", true, false, {L".avif"}},
        // HEIC/HEIF: the engine decodes it but ships no HEVC encoder, so it is input-only.
        {"heic", L"HEIC", false, false, {L".heic", L".heif"}},
        // GIF (and its animations): input-only; the engine converts the first frame or every frame.
        {"gif", L"GIF", false, false, {L".gif"}},
    };
    return formats;
}

const FormatInfo* FindOutputFormat(std::string_view id) {
    for (const auto& format : AllFormats()) {
        if (format.canOutput && format.id == id) return &format;
    }
    return nullptr;
}

std::optional<std::string_view> FormatIdForPath(std::wstring_view path) {
    const std::wstring extension = ExtensionOf(path);
    if (extension.empty()) return std::nullopt;
    for (const auto& format : AllFormats()) {
        for (const auto candidate : format.extensions) {
            if (candidate == extension) return format.id;
        }
    }
    return std::nullopt;
}

bool IsSupportedInput(std::wstring_view path) { return FormatIdForPath(path).has_value(); }

std::vector<std::wstring> SupportedInputExtensions() {
    std::vector<std::wstring> extensions;
    for (const auto& format : AllFormats()) {
        for (const auto extension : format.extensions) extensions.emplace_back(extension);
    }
    return extensions;
}

}  // namespace qic
