#include "pch.h"
#include "logger.h"

#include <codecvt>
#include <locale>

#include "hook_func.h"
#include "origin.h"

ProcessTracer::Logger ProcessTracer::Logger::g_logger(0, 0);

static std::wstring ConvertPipePath(const std::string& ansiPath)
{
	std::wstring result;

	std::string prefix = R"(\\.\pipe\)";
	if (ansiPath.find(prefix) == 0)
	{
		std::string pipeName = ansiPath.substr(prefix.length());
		result = L"\\Device\\NamedPipe\\" + std::wstring(pipeName.begin(), pipeName.end());
	}
	else
	{
		result = std::wstring(ansiPath.begin(), ansiPath.end());
	}

	return result;
}

BOOL ProcessTracer::Logger::WriteToPipe(const char* prefix, const char* message, const char* postfix) const
{
	if (m_process_tracer_pid == 0)
		return FALSE;
	std::string fullMessage = "pid:" + std::to_string(m_pid) + " " + std::string(prefix) + message + postfix;
	DWORD bytesWritten = 0;
	HANDLE hPipe = CreateFileA(
		pipe_file_string.c_str(),
		GENERIC_WRITE,
		0,
		nullptr,
		OPEN_EXISTING,
		0,
		nullptr);
	IO_STATUS_BLOCK iosb = {};
	auto status = NtWriteFile(
		hPipe,
		nullptr, // Event
		nullptr, // ApcRoutine
		nullptr, // ApcContext
		&iosb,
		(PVOID)fullMessage.c_str(),
		fullMessage.length(),
		nullptr,
		nullptr // Key
	);

	BOOL result = (status == 0); // STATUS_SUCCESS == 0
	bytesWritten = (DWORD)iosb.Information;

	CloseHandle(hPipe);
	return result && (bytesWritten == fullMessage.length());
}

BOOL ProcessTracer::Logger::WriteToPipeNtCreateProcess(const char* prefix, const char* message,
                                                       const char* postfix) const
{
	if (m_process_tracer_pid == 0)
		return FALSE;
	std::string fullMessage = "pid:" + std::to_string(m_pid) + " " + std::string(prefix) + message + postfix;
	HANDLE hPipe = nullptr;
	UNICODE_STRING uPipeName;
	RtlInitUnicodeString(&uPipeName, pipe_file_w_string.c_str());

	OBJECT_ATTRIBUTES objAttr;
	InitializeObjectAttributes(&objAttr, &uPipeName, OBJ_CASE_INSENSITIVE, NULL, NULL);

	IO_STATUS_BLOCK ioStatusBlock;
	NTSTATUS status = NtCreateFile(
		&hPipe,
		GENERIC_WRITE | SYNCHRONIZE,
		&objAttr,
		&ioStatusBlock,
		nullptr,
		0,
		0,
		FILE_OPEN,
		FILE_SYNCHRONOUS_IO_NONALERT,
		nullptr,
		0
	);
	if (!NT_SUCCESS(status))
	{
		if (hPipe)
		{
			CloseHandle(hPipe);
		}
		return FALSE; // Failed to open pipe
	}
	IO_STATUS_BLOCK iosb = {0};
	status = NtWriteFile(
		hPipe,
		nullptr, // Event
		nullptr, // ApcRoutine
		nullptr, // ApcContext
		&iosb,
		PVOID(fullMessage.c_str()),
		fullMessage.length(),
		nullptr,
		nullptr // Key
	);

	BOOL result = (status == 0); // STATUS_SUCCESS == 0
	auto bytesWritten = (DWORD)iosb.Information;

	CloseHandle(hPipe);
	return result && (bytesWritten == fullMessage.length());
}

ProcessTracer::Logger::Logger(int process_tracer_pid, int pid)
{
	if (process_tracer_pid == 0)
		return;
	m_process_tracer_pid = process_tracer_pid;
	m_pid = pid;
	pipe_file_string = "\\\\.\\pipe\\ProcessTracerPipe:" + std::to_string(m_process_tracer_pid);
	pipe_file_w_string = ConvertPipePath(pipe_file_string);
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

BOOL ProcessTracer::Logger::HookNtCreateProcessInfo(const char* hook_func_name, const char* message) const
{
	const std::string full_message = std::string(hook_func_name) + " " + message;
	return WriteToPipeNtCreateProcess("[Hook] ", full_message.c_str(), "\n");
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

void LogHookNtCreateProcessInfo(const char* hook_func_name, const char* msg)
{
	auto _ = ProcessTracer::Logger::g_logger.HookNtCreateProcessInfo(hook_func_name, msg);
}

void LogHookError(const char* hook_func_name, const char* msg)
{
	auto _ = ProcessTracer::Logger::g_logger.HookError(hook_func_name, msg);
}
