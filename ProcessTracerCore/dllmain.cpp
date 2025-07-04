// dllmain.cpp : 定義 DLL 應用程式的進入點。
#include "pch.h"
#include "detours.h"
#include <strsafe.h>
#include "origin.cpp"
#include "hook_func.cpp"

static HMODULE s_hModule = nullptr;
static WCHAR s_wzDllPath[MAX_PATH];
static CHAR s_szDllPath[MAX_PATH];

LONG DetoursAttach()
{
	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());
	PVOID* ppbFailedPointer = nullptr;
	LONG error = DetourTransactionCommitEx(&ppbFailedPointer);
	if (error != 0)
	{
		return error;
	}
	return 0;
}

LONG DetoursDetach()
{
	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());
	if (DetourTransactionCommit() != 0)
	{
		PVOID* ppbFailedPointer = nullptr;
		LONG error = DetourTransactionCommitEx(&ppbFailedPointer);
		return error;
	}
	return 0;
}

BOOL ProcessAttach(HMODULE hDll)
{
	s_hModule = hDll;
	WCHAR wzExeName[MAX_PATH];
	Real_GetModuleFileNameW(hDll, s_wzDllPath, ARRAYSIZE(s_wzDllPath));
	Real_GetModuleFileNameW(nullptr, wzExeName, ARRAYSIZE(wzExeName));
	StringCchPrintfA(s_szDllPath, ARRAYSIZE(s_szDllPath), "%ls", s_wzDllPath);
}

BOOL APIENTRY DllMain(HMODULE hModule,
                      DWORD ul_reason_for_call,
                      LPVOID lpReserved
)
{
	if (DetourIsHelperProcess())
	{
		return TRUE;
	}

	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		DetourRestoreAfterWith();
		return ProcessAttach(hModule);
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}
