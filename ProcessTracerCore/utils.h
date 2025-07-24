#pragma once
#include <string>
#include <vector>

std::string ConvertWStringToString(LPCWSTR wstr, UINT codepage = CP_UTF8);
std::vector<std::string> SplitBySpace(const std::string& input);
bool StartsWith(const std::string& str, const std::string& prefix);
bool EndsWith(const std::string& str, const std::string& suffix);
std::wstring ReplaceWString(std::wstring origin, std::wstring find, std::wstring replace);
std::wstring ConvertStringToWString(const std::string& origin, UINT code_page = CP_UTF8);
