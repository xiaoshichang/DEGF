#include "timer/TimerManager.h"

#include <stdexcept>
#include <utility>
#include <vector>

namespace de::server::engine
{
	TimerManager::TimerEntry::TimerEntry(
		asio::io_context& ioContext,
		TimerID timerId,
		std::chrono::milliseconds timerDelay,
		TimerCallback timerCallback,
		bool repeated
	)
		: id(timerId)
		, timer(ioContext)
		, delay(timerDelay)
		, callback(std::move(timerCallback))
		, repeat(repeated)
	{
	}

	TimerManager::TimerManager(asio::io_context& ioContext)
		: ioContext_(ioContext)
	{
	}

	TimerManager::~TimerManager()
	{
		Shutdown();
	}

	TimerManager::TimerID TimerManager::AddTimer(
		std::chrono::milliseconds delay,
		TimerCallback callback,
		bool repeat
	)
	{
		if (!callback)
		{
			throw std::invalid_argument("Timer callback must not be empty.");
		}

		if (delay.count() < 0)
		{
			throw std::invalid_argument("Timer delay must not be negative.");
		}

		std::shared_ptr<TimerEntry> timerEntry;
		if (shuttingDown_)
		{
			throw std::runtime_error("TimerManager is shutting down.");
		}

		const TimerID timerId = nextTimerId_++;
		timerEntry = std::make_shared<TimerEntry>(ioContext_, timerId, delay, std::move(callback), repeat);
		timers_.emplace(timerId, timerEntry);

		ArmTimer(timerEntry, delay);
		return timerId;
	}

	bool TimerManager::CancelTimer(TimerID timerId)
	{
		const auto iterator = timers_.find(timerId);
		if (iterator == timers_.end())
		{
			return false;
		}

		const auto timerEntry = iterator->second;
		timers_.erase(iterator);

		timerEntry->timer.cancel();
		return true;
	}

	std::size_t TimerManager::CancelAllTimers()
	{
		std::vector<std::shared_ptr<TimerEntry>> timerEntries;
		timerEntries.reserve(timers_.size());
		for (auto& [timerId, timerEntry] : timers_)
		{
			(void)timerId;
			timerEntries.push_back(timerEntry);
		}
		timers_.clear();

		for (const auto& timerEntry : timerEntries)
		{
			timerEntry->timer.cancel();
		}

		return timerEntries.size();
	}

	bool TimerManager::HasTimer(TimerID timerId) const
	{
		return timers_.find(timerId) != timers_.end();
	}

	void TimerManager::Shutdown()
	{
		if (shuttingDown_)
		{
			return;
		}

		shuttingDown_ = true;

		CancelAllTimers();
	}

	void TimerManager::ArmTimer(const std::shared_ptr<TimerEntry>& timerEntry, std::chrono::milliseconds delay)
	{
		timerEntry->timer.expires_after(delay);
		timerEntry->timer.async_wait(
			[this, timerEntry](const boost::system::error_code& errorCode)
			{
				OnTimerFired(timerEntry, errorCode);
			}
		);
	}

	void TimerManager::OnTimerFired(const std::shared_ptr<TimerEntry>& timerEntry, const boost::system::error_code& errorCode)
	{
		if (errorCode == asio::error::operation_aborted)
		{
			return;
		}

		if (errorCode)
		{
			return;
		}

		TimerCallback callback;
		bool repeat = false;
		std::chrono::milliseconds delay{ 0 };
		if (shuttingDown_)
		{
			return;
		}

		const auto iterator = timers_.find(timerEntry->id);
		if (iterator == timers_.end() || iterator->second.get() != timerEntry.get())
		{
			return;
		}

		callback = timerEntry->callback;
		repeat = timerEntry->repeat;
		delay = timerEntry->delay;

		if (!repeat)
		{
			timers_.erase(iterator);
		}

		callback(timerEntry->id);

		if (!repeat)
		{
			return;
		}

		if (shuttingDown_)
		{
			return;
		}

		const auto repeatIterator = timers_.find(timerEntry->id);
		if (repeatIterator == timers_.end() || repeatIterator->second.get() != timerEntry.get())
		{
			return;
		}

		ArmTimer(timerEntry, delay);
	}
}
