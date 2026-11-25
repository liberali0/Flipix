#include "core/json.h"

#include "test_harness.h"

using qic::JsonValue;
using qic::ParseJson;

TEST(ParseJson_ReadsNestedDocument) {
    const auto doc = ParseJson(R"( {"a": [1, -2.5e2, true, false, null], "b": {"c": "d"}} )");
    CHECK(doc.has_value());
    if (!doc) return;
    CHECK(doc->type == JsonValue::Type::Object);
    const JsonValue* a = doc->Find("a");
    CHECK(a != nullptr && a->type == JsonValue::Type::Array && a->array.size() == 5);
    if (a != nullptr && a->array.size() == 5) {
        CHECK_EQ(a->array[0].number, 1.0);
        CHECK_EQ(a->array[1].number, -250.0);
        CHECK(a->array[2].type == JsonValue::Type::Bool && a->array[2].boolean);
        CHECK(a->array[3].type == JsonValue::Type::Bool && !a->array[3].boolean);
        CHECK(a->array[4].type == JsonValue::Type::Null);
    }
    const JsonValue* b = doc->Find("b");
    CHECK(b != nullptr && b->Find("c") != nullptr && b->Find("c")->string == "d");
}

TEST(ParseJson_DecodesEscapesToUtf8) {
    const auto doc = ParseJson(R"(["\"\\\/\b\f\n\r\t", "\uD55C\uAE00", "\uD83D\uDCF7", "\u0070ng"])");
    CHECK(doc.has_value());
    if (!doc || doc->array.size() != 4) return;
    CHECK_EQ(doc->array[0].string, std::string("\"\\/\b\f\n\r\t"));
    CHECK_EQ(doc->array[1].string, std::string("\xED\x95\x9C\xEA\xB8\x80"));
    CHECK_EQ(doc->array[2].string, std::string("\xF0\x9F\x93\xB7"));
    CHECK_EQ(doc->array[3].string, std::string("png"));
}

TEST(ParseJson_KeepsRawUtf8) {
    const auto doc = ParseJson("{\"k\": \"\xED\x95\x9C\"}");
    CHECK(doc.has_value() && doc->Find("k") != nullptr && doc->Find("k")->string == "\xED\x95\x9C");
}

TEST(ParseJson_AllowsLeadingBom) {
    CHECK(ParseJson("\xEF\xBB\xBF{}").has_value());
}

TEST(ParseJson_FindIsCaseInsensitiveAndLastDuplicateWins) {
    const auto doc = ParseJson(R"({"QuickMenu": 1, "quickmenu": 2})");
    CHECK(doc.has_value() && doc->Find("quickMenu") != nullptr && doc->Find("quickMenu")->number == 2.0);
    CHECK(doc.has_value() && doc->Find("missing") == nullptr);
}

TEST(ParseJson_RejectsMalformedDocuments) {
    for (const char* bad : {"", "   ", "{", "[1,]", "{\"a\":1,}", "{'a':1}", "[01]", "[1.]", "[.5]",
                            "[-]", "[1e]", "tru", "nul", "\"abc", "\"a\nb\"", "\"\\x\"", "\"\\u12\"",
                            "{} x", "[1] [2]", "{\"a\" 1}", "{1:2}", "[\"\\uDC00\"]", "[\"\\uD800\"]",
                            "[\"\\uD800\\u0041\"]"}) {
        if (ParseJson(bad).has_value()) {
            ::testing::ReportFailure(__FILE__, __LINE__, std::string("accepted: ") + bad);
        }
    }
}

TEST(ParseJson_RejectsExcessiveNesting) {
    const std::string deep = std::string(qic::kMaxJsonDepth + 1, '[') + std::string(qic::kMaxJsonDepth + 1, ']');
    CHECK(!ParseJson(deep).has_value());
    const std::string ok = std::string(qic::kMaxJsonDepth, '[') + std::string(qic::kMaxJsonDepth, ']');
    CHECK(ParseJson(ok).has_value());
}

TEST(ParseJson_ReadsScalarsAtTopLevel) {
    const auto doc = ParseJson(" 42 ");
    CHECK(doc.has_value() && doc->type == JsonValue::Type::Number && doc->number == 42.0);
    const auto text = ParseJson("\"x\"");
    CHECK(text.has_value() && text->type == JsonValue::Type::String && text->string == "x");
}
