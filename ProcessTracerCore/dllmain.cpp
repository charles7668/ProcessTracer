#include "pch.h"

#include "detours.h"
#include <strsafe.h>
#include "constants.h"
#include "hook_func.h"
#include "hook_info.h"
#include "logger.h"
#include "origin.h"
#include "utils.h"


EXTERN_C extern PVOID __imp_NtWriteFile;
EXTERN_C extern PVOID __imp_ZwWriteFile;
EXTERN_C extern PVOID __imp_NtCreateSection;
EXTERN_C extern PVOID __imp_NtCreateFile;
EXTERN_C extern PVOID __imp_ZwCreateSection;
EXTERN_C extern PVOID __imp_NtCreateSectionEx;
EXTERN_C extern PVOID __imp_NtMapViewOfSection;
EXTERN_C extern PVOID __imp_NtCreateUserProcess;


namespace
{
	DWORD oldProtect = 0;

	LONG DetoursAttach()
	{
		LogInfo("Attaching functions...");
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		// NOLINTBEGIN 
		DetourAttach(&(PVOID&)RealCreateProcessInternalW, HookCreateProcessInternalW);
		DetourAttach(&(PVOID&)RealExitProcess, HookExitProcess);
		DetourAttach(&(PVOID&)RealShellExecuteExW, HookShellExecuteExW);
		// DetourAttach(&(PVOID&)RealWriteFile, HookWriteFile);
		// DetourAttach(&(PVOID&)RealWriteFileEx, HookWriteFileEx);
		// DetourAttach(&(PVOID&)RealNtWriteFile, HookNtWriteFile);
		DetourAttach(&__imp_NtWriteFile, HookNtWriteFile);
		DetourAttach(&__imp_ZwWriteFile, HookZwWriteFile);
		DetourAttach(&__imp_NtCreateSection, HookNtCreateSection);
		DetourAttach(&__imp_NtCreateFile, HookNtCreateFile);
		DetourAttach(&__imp_ZwCreateSection, HookZwCreateSection);
		DetourAttach(&__imp_NtCreateSectionEx, HookNtCreateSectionEx);
		DetourAttach(&__imp_NtMapViewOfSection, HookNtMapViewOfSection);
		DetourAttach(&__imp_NtCreateUserProcess, HookNtCreateUserProcess);

		// NOLINTEND

		PVOID* ppbFailedPointer = nullptr;
		LONG error = DetourTransactionCommitEx(&ppbFailedPointer);
		VirtualProtect(&__imp_NtWriteFile, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_ZwWriteFile, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_NtCreateSection, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_NtCreateFile, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_ZwCreateSection, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_NtCreateSectionEx, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_NtMapViewOfSection, sizeof(PVOID), oldProtect, nullptr);
		VirtualProtect(&__imp_NtCreateUserProcess, sizeof(PVOID), oldProtect, nullptr);
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
		DetourDetach(&(PVOID&)RealShellExecuteExW, HookShellExecuteExW);
		// DetourDetach(&(PVOID&)RealWriteFile, HookWriteFile);
		// DetourDetach(&(PVOID&)RealWriteFileEx, HookWriteFileEx);
		// DetourDetach(&(PVOID&)RealNtWriteFile, HookNtWriteFile);
		DetourDetach(&__imp_NtWriteFile, HookNtWriteFile);
		DetourDetach(&__imp_ZwWriteFile, HookZwWriteFile);
		DetourDetach(&__imp_NtCreateSection, HookNtCreateSection);
		DetourDetach(&__imp_NtCreateFile, HookNtCreateFile);
		DetourDetach(&__imp_ZwCreateSection, HookZwCreateSection);
		DetourDetach(&__imp_NtCreateSectionEx, HookNtCreateSectionEx);
		DetourDetach(&__imp_NtMapViewOfSection, HookNtMapViewOfSection);
		DetourDetach(&__imp_NtCreateUserProcess, HookNtCreateUserProcess);

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
		const char* payload = static_cast<const char*>(DetourFindPayloadEx(GUID_PIPE_HANDLE, &cb_data));
		if (cb_data == 0)
		{
			return FALSE;
		}

		const std::string payload_string(payload, cb_data);
		auto splits = SplitBySpace(payload_string);
		memset(hook_info->process_tracer_pid_string_buffer, 0, sizeof(hook_info->process_tracer_pid_string_buffer));
		memcpy(hook_info->process_tracer_pid_string_buffer, splits[0].c_str(), cb_data);
		hook_info->process_tracer_pid_string_buffer[cb_data] = 0;

		const auto pid_value = std::stoi(splits[0]);
		hook_info->process_tracer_pid = pid_value;

		DWORD current_pid = GetCurrentProcessId();
		ProcessTracer::Logger::g_logger = ProcessTracer::Logger(pid_value , current_pid);
		std::string msg = "ProcessTracerCore attached to process: " + std::to_string(current_pid) +
			", Process Tracer PID: " + std::to_string(hook_info->process_tracer_pid);
		LogInfo(msg.c_str());
		LogInfo(msg.c_str());
		hook_info->can_elevate = splits[1][0] == '0';
		msg = "ProcessTracerCore can elevate: " + std::to_string(hook_info->can_elevate);
		LogInfo(msg.c_str());

		return TRUE;
	}

	BOOL FindWin32Func()
	{
		PVOID p_createProcessInternalW = DetourFindFunction(
			"kernelbase.dll", "CreateProcessInternalW");
		RealCreateProcessInternalW = reinterpret_cast<CreateProcessInternalWFn>(p_createProcessInternalW);
		HMODULE hKernelBase = GetModuleHandleW(L"KernelBase.dll");
		RealCreateFileMappingW = (decltype(&CreateFileMappingW))GetProcAddress(hKernelBase, "CreateFileMappingW");
		char buffer[100];
		VirtualProtect(&__imp_NtWriteFile, sizeof(PVOID), PAGE_EXECUTE_READWRITE, &oldProtect);
		VirtualProtect(&__imp_ZwWriteFile, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);
		VirtualProtect(&__imp_NtCreateSection, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);
		VirtualProtect(&__imp_NtCreateFile, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);
		VirtualProtect(&__imp_ZwCreateSection, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);
		VirtualProtect(&__imp_NtCreateSectionEx, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);
		VirtualProtect(&__imp_NtMapViewOfSection, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);
		VirtualProtect(&__imp_NtCreateUserProcess, sizeof(PVOID), PAGE_EXECUTE_READWRITE, nullptr);

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
		ProcessTracer::Logger::g_logger = ProcessTracer::Logger(0, 0);
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
