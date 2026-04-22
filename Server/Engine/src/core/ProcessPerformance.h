#pragma once

#include <cstdint>

namespace de::server::engine
{
	struct ProcessPerformanceSnapshot
	{
		std::uint64_t workingSetBytes = 0;
	};

	ProcessPerformanceSnapshot CollectProcessPerformanceSnapshot();
}
