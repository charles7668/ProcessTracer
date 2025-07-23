#pragma once
#include <string>

namespace ProcessTracer
{
	class Logger
	{
		int m_process_tracer_pid = 0;
		int m_pid = 0;
		std::string pipe_file_string;
		std::wstring pipe_file_w_string;

		BOOL WriteToPipe(const char* prefix, const char* message, const char* postfix) const;
		BOOL WriteToPipeNtCreateProcess(const char* prefix, const char* message, const char* postfix) const;

	public:
		static Logger g_logger;

		Logger(int process_tracer_pid, int pid);

		BOOL Info(const char* message) const;
		BOOL Info(const wchar_t* message) const;
		BOOL Error(const char* message) const;
		BOOL Error(const wchar_t* message) const;
		BOOL HookInfo(const char* hook_func_name, const char* message) const;
		BOOL HookNtCreateProcessInfo(const char* hook_func_name, const char* message) const;
		BOOL HookError(const char* hook_func_name, const char* message) const;
	};
}

// wrap the g_logger call in Logger class
VOID LogError(const char* msg);
VOID LogInfo(const char* msg);
VOID LogInfoF(const char* msg , ...);
VOID LogErrorF(const char* msg, ...);
VOID LogHookInfo(const char* hook_func_name, const char* msg);
VOID LogHookNtCreateProcessInfo(const char* hook_func_name, const char* msg);
VOID LogHookError(const char* hook_func_name, const char* msg);
