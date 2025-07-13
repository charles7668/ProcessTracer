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
