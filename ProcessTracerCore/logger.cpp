#include "pch.h"
#include "logger.h"

ProcessTracer::Logger ProcessTracer::Logger::g_logger = 0;

BOOL ProcessTracer::Logger::WriteToPipe(const char* prefix, const char* message, const char* postfix) const
{
	if (m_Pid == 0)
		return FALSE;
	std::string fullMessage = std::string(prefix) + message + postfix;
	DWORD bytesWritten = 0;
	HANDLE hPipe = CreateFileA(
		pipe_file_string.c_str(),
		GENERIC_WRITE,
		0,
		nullptr,
		OPEN_EXISTING,
		0,
		nullptr);
	BOOL result = WriteFile(hPipe, fullMessage.c_str(), fullMessage.length(),
	                        &bytesWritten, nullptr);
	CloseHandle(hPipe);
	return result && (bytesWritten == fullMessage.length());
}

ProcessTracer::Logger::Logger(int pid)
{
	if (pid == 0)
		return;
	m_Pid = pid;
	pipe_file_string = "\\\\.\\pipe\\ProcessTracerPipe:" + std::to_string(m_Pid);
	// pipe_file_string = "\\\\.\\pipe\\ProcessTracerPipe";
}

BOOL ProcessTracer::Logger::Info(const char* message) const
{
	return WriteToPipe("[Info] ", message, "\n");
}

BOOL ProcessTracer::Logger::Info(const wchar_t* message) const
{
	std::wstring temp(message);
	return Info(std::string(temp.begin(), temp.end()).c_str());
}

BOOL ProcessTracer::Logger::Error(const char* message) const
{
	return WriteToPipe("[Error] ", message, "\n");
}

BOOL ProcessTracer::Logger::Error(const wchar_t* message) const
{
	std::wstring temp(message);
	return Error(std::string(temp.begin(), temp.end()).c_str());
}

BOOL ProcessTracer::Logger::HookInfo(const char* hook_func_name, const char* message) const
{
	const std::string full_message = std::string(hook_func_name) + " " + message;
	return WriteToPipe("[Hook] ", full_message.c_str(), "\n");
}

BOOL ProcessTracer::Logger::HookError(const char* hook_func_name, const char* message) const
{
	const std::string full_message = std::string(hook_func_name) + " " + message;
	return WriteToPipe("[Hook Error] ", full_message.c_str(), "\n");
}

void LogError(const char* msg)
{
	auto _ = ProcessTracer::Logger::g_logger.Error(msg);
}

void LogInfo(const char* msg)
{
	auto _ = ProcessTracer::Logger::g_logger.Info(msg);
}

void LogHookInfo(const char* hook_func_name, const char* msg)
{
	auto _ = ProcessTracer::Logger::g_logger.HookInfo(hook_func_name, msg);
}

void LogHookError(const char* hook_func_name, const char* msg)
{
	auto _ = ProcessTracer::Logger::g_logger.HookError(hook_func_name, msg);
}
