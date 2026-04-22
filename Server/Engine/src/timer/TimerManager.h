#pragma once

#include "core/BoostAsio.h"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <unordered_map>

namespace de::server::engine
{
	class TimerManager
	{
	public:
		using TimerID = std::uint64_t;
		using TimerCallback = std::function<void(TimerID)>;

		explicit TimerManager(asio::io_context& ioContext);
		~TimerManager();

		TimerID AddTimer(std::chrono::milliseconds delay, TimerCallback callback, bool repeat = false);
		bool CancelTimer(TimerID timerId);
		std::size_t CancelAllTimers();
		bool HasTimer(TimerID timerId) const;
		void Shutdown();

	private:
		struct TimerEntry
		{
			TimerEntry(
				asio::io_context& ioContext,
				TimerID timerId,
				std::chrono::milliseconds timerDelay,
				TimerCallback timerCallback,
				bool repeated
			);

			TimerID id = 0;
			asio::steady_timer timer;
			std::chrono::milliseconds delay{ 0 };
			TimerCallback callback;
			bool repeat = false;
		};

		void ArmTimer(const std::shared_ptr<TimerEntry>& timerEntry, std::chrono::milliseconds delay);
		void OnTimerFired(const std::shared_ptr<TimerEntry>& timerEntry, const boost::system::error_code& errorCode);

		asio::io_context& ioContext_;
		TimerID nextTimerId_ = 1;
		bool shuttingDown_ = false;
		std::unordered_map<TimerID, std::shared_ptr<TimerEntry>> timers_;
	};
}
