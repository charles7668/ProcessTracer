#include "pch.h"

#include "detours.h"
#include <strsafe.h>
#include "constants.h"
#include "hook_func.h"
#include "hook_info.h"
#include "logger.h"
#include "origin.h"

namespace
{
	LONG DetoursAttach()
	{
		LogInfo("Attaching functions...");
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		// NOLINTBEGIN 
		DetourAttach(&(PVOID&)RealCreateProcessInternalW, HookCreateProcessInternalW);
		DetourAttach(&(PVOID&)RealExitProcess, HookExitProcess);
		// NOLINTEND

		PVOID* ppbFailedPointer = nullptr;
		LONG error = DetourTransactionCommitEx(&ppbFailedPointer);
		if (error != 0)
		{
			LogError(("DetourTransactionCommitEx failed with error code: " + std::to_string(error)).c_str());
			return error;
		}
		LogInfo("DetoursAttach completed successfully.");
		return 0;
	}

	LONG DetoursDetach()
	{
		LogInfo("Detaching functions...");
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());
		// NOLINTBEGIN 
		DetourDetach(&(PVOID&)RealCreateProcessInternalW, HookCreateProcessInternalW);
		DetourDetach(&(PVOID&)RealExitProcess, HookExitProcess);
		// NOLINTEND
		auto error = DetourTransactionCommit();
		if (error != 0)
		{
			LogError(("DetourTransactionCommit failed with error code: " + std::to_string(error)).c_str());
			return error;
		}
		LogInfo("DetoursDetach completed successfully.");
		return 0;
	}


	BOOL ConnectToPipe()
	{
		const auto hook_info = GetHookInfoInstance();
		if (hook_info->process_tracer_pid)
			return TRUE;
		DWORD cb_data = 0;
		const char* pid_payload = static_cast<const char*>(DetourFindPayloadEx(GUID_PIPE_HANDLE, &cb_data));
		if (cb_data == 0)
		{
			return FALSE;
		}

		const std::string pid_string(pid_payload, cb_data);
		memset(hook_info->process_tracer_pid_string_buffer, 0, sizeof(hook_info->process_tracer_pid_string_buffer));
		memcpy(hook_info->process_tracer_pid_string_buffer, pid_string.c_str(), cb_data);
		hook_info->process_tracer_pid_string_buffer[cb_data] = 0;

		const auto pid_value = std::stoi(pid_string);
		hook_info->process_tracer_pid = pid_value;

		ProcessTracer::Logger::g_logger = ProcessTracer::Logger(pid_value);
		DWORD current_pid = GetCurrentProcessId();
		std::string msg = "ProcessTracerCore attached to process: " + std::to_string(current_pid) +
			", Process Tracer PID: " + std::to_string(hook_info->process_tracer_pid);
		LogInfo(msg.c_str());

		return TRUE;
	}

	BOOL FindWin32Func()
	{
		PVOID p_createProcessInternalW = DetourFindFunction(
			"kernelbase.dll", "CreateProcessInternalW");
		RealCreateProcessInternalW = reinterpret_cast<CreateProcessInternalWFn>(p_createProcessInternalW);
		return TRUE;
	}

	BOOL ThreadAttach()
	{
		ConnectToPipe();
		return TRUE;
	}

	BOOL ThreadDetach()
	{
		return TRUE;
	}

	BOOL ProcessAttach(const HMODULE dll_handle)
	{
		ThreadAttach();
		const auto hook_info = GetHookInfoInstance();
		RealGetModuleFileNameA(dll_handle, hook_info->dll_path, MAX_PATH);
		RealGetModuleFileNameA(nullptr, hook_info->exe_name, MAX_PATH);
		FindWin32Func();
		DetoursAttach();
		return TRUE;
	}

	BOOL ProcessDetach(HMODULE hDll)
	{
		DetoursDetach();
		auto hook_info = GetHookInfoInstance();
		hook_info->process_tracer_pid = 0;
		ProcessTracer::Logger::g_logger = 0;
		return TRUE;
	}
}


BOOL APIENTRY DllMain(HINSTANCE hModule, DWORD dwReason, PVOID lpReserved) // NOLINT
{
	if (DetourIsHelperProcess())
	{
		return TRUE;
	}

	switch (dwReason)
	{
	case DLL_PROCESS_ATTACH:
		DetourRestoreAfterWith();
		return ProcessAttach(hModule);
	case DLL_THREAD_ATTACH:
		return ThreadAttach();
	case DLL_THREAD_DETACH:
		return ThreadDetach();
	case DLL_PROCESS_DETACH:
		return ProcessDetach(hModule);
	default: ;
	}
	return TRUE;
}
