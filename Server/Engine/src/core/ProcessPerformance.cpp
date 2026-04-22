#include "core/ProcessPerformance.h"

#ifdef _WIN32
#include <windows.h>
#include <psapi.h>
#endif

namespace de::server::engine
{
	ProcessPerformanceSnapshot CollectProcessPerformanceSnapshot()
	{
		ProcessPerformanceSnapshot snapshot;

#ifdef _WIN32
		PROCESS_MEMORY_COUNTERS_EX memoryCounters{};
		if (::GetProcessMemoryInfo(
			::GetCurrentProcess(),
			reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memoryCounters),
			sizeof(memoryCounters)
		) != FALSE)
		{
			snapshot.workingSetBytes = static_cast<std::uint64_t>(memoryCounters.WorkingSetSize);
		}
#endif

		return snapshot;
	}
}
