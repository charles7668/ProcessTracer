#pragma once
#include <string>
#include <vector>

std::string ConvertWStringToString(LPCWSTR wstr, UINT codepage = CP_UTF8);
std::vector<std::string> SplitBySpace(const std::string& input);
bool StartsWith(const std::string& str, const std::string& prefix);
