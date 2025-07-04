#include "pch.h"
#include "hook_info.h"

namespace
{
	HookInfo g_hookInfoInstance;
}

HookInfo* GetHookInfoInstance()
{
	return &g_hookInfoInstance;
}
