#include "pch.h"
#include <string>

#include "framework.h"
#include "utils.h"

#include <vector>

std::string ConvertWStringToString(LPCWSTR wstr, UINT codepage)
{
	int len = WideCharToMultiByte(codepage, 0, wstr, -1, nullptr, 0, NULL, NULL);
	std::string str(len, 0);
	WideCharToMultiByte(codepage, 0, wstr, -1, const_cast<LPSTR>(str.data()), len, NULL, NULL);
	if (!str.empty())
		str.pop_back();
	return str;
}

std::vector<std::string> SplitBySpace(const std::string& input)
{
	std::vector<std::string> result;
	size_t pos = 0;
	const std::string delimiter = " ";

	while (true)
	{
		size_t next = input.find(delimiter, pos);
		if (next == std::string::npos)
		{
			result.push_back(input.substr(pos));
			break;
		}
		result.push_back(input.substr(pos, next - pos));
		pos = next + delimiter.length();
	}

	return result;
}

bool StartsWith(const std::string& str, const std::string& prefix) {
	return str.size() >= prefix.size() &&
		str.compare(0, prefix.size(), prefix) == 0;
}

bool EndsWith(const std::string& str, const std::string& suffix) {
    return str.size() >= suffix.size() &&
           str.compare(str.size() - suffix.size(), suffix.size(), suffix) == 0;
}

std::wstring ReplaceWString(std::wstring origin, std::wstring find, std::wstring replace)
{
	auto result = origin;
	size_t pos = 0;
	while ((pos = result.find(find, pos)) != std::wstring::npos)
	{
		result.replace(pos, find.length(), replace);
		pos += replace.length();
	}
	return result;
}

std::wstring ConvertStringToWString(const std::string& origin , UINT code_page)
{
	int wide_char_len = MultiByteToWideChar(
		code_page,
		0,
		origin.c_str(),
		-1,
		nullptr,
		0
	);

	if (wide_char_len == 0)
	{
		return L"";
	}

	std::wstring wide_str(wide_char_len, L'\0');

	int result = MultiByteToWideChar(
		code_page,
		0,
		origin.c_str(),
		-1,
		const_cast<LPWSTR>(wide_str.data()),
		wide_char_len
	);

	if (result == 0)
	{
		return L"";
	}
	if (!wide_str.empty())
		wide_str.pop_back();
	return wide_str;
}
