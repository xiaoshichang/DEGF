#include "config/ClusterConfig.h"

#include <boost/json.hpp>

#include <fstream>
#include <sstream>
#include <stdexcept>

namespace de::server::engine::config
{
	namespace
	{
		namespace json = boost::json;
		constexpr std::string_view kCanonicalGmServerId = "GM";

		std::string GetString(const json::object& object, std::string_view key)
		{
			return json::value_to<std::string>(object.at(key));
		}

		int GetInt(const json::object& object, std::string_view key)
		{
			return json::value_to<int>(object.at(key));
		}

		bool GetBool(const json::object& object, std::string_view key)
		{
			return object.at(key).as_bool();
		}

		bool GetBoolOrDefault(const json::object& object, std::string_view key, bool defaultValue)
		{
			const json::value* value = object.if_contains(key);
			if (value == nullptr)
			{
				return defaultValue;
			}

			return value->as_bool();
		}

		bool GetLoggingEnableFile(const json::object& object)
		{
			if (const json::value* value = object.if_contains("EnableFile"))
			{
				return value->as_bool();
			}

			return GetBoolOrDefault(object, "enableFile", true);
		}

		std::uint16_t GetUInt16(const json::object& object, std::string_view key)
		{
			return static_cast<std::uint16_t>(json::value_to<std::uint64_t>(object.at(key)));
		}

		std::uint16_t GetUInt16OrDefault(const json::object& object, std::string_view key, std::uint16_t defaultValue)
		{
			const json::value* value = object.if_contains(key);
			if (value == nullptr)
			{
				return defaultValue;
			}

			return static_cast<std::uint16_t>(json::value_to<std::uint64_t>(*value));
		}

		EndpointConfig ParseEndpointConfig(const json::object& object)
		{
			return EndpointConfig{
				GetString(object, "host"),
				GetUInt16(object, "port")
			};
		}

		NetworkConfig ParseNetworkConfig(const json::object& object)
		{
			return NetworkConfig{
				ParseEndpointConfig(object.at("listenEndpoint").as_object())
			};
		}

		TelnetConfig ParseTelnetConfig(const json::object& object)
		{
			return TelnetConfig{
				GetUInt16OrDefault(object, "port", static_cast<std::uint16_t>(0))
			};
		}

		HttpConfig ParseHttpConfig(const json::object& object)
		{
			return HttpConfig{
				ParseEndpointConfig(object.at("listenEndpoint").as_object())
			};
		}

		GMConfig ParseGMConfig(const json::object& object)
		{
			return GMConfig{
				ParseNetworkConfig(object.at("innerNetwork").as_object()),
				ParseNetworkConfig(object.at("controlNetwork").as_object()),
				object.if_contains("http") != nullptr ? ParseHttpConfig(object.at("http").as_object()) : HttpConfig{},
				object.if_contains("telnet") != nullptr ? ParseTelnetConfig(object.at("telnet").as_object()) : TelnetConfig{}
			};
		}

		GateConfig ParseGateConfig(const json::object& object)
		{
			return GateConfig{
				ParseNetworkConfig(object.at("innerNetwork").as_object()),
				ParseNetworkConfig(object.at("authNetwork").as_object()),
				ParseNetworkConfig(object.at("clientNetwork").as_object()),
				object.if_contains("telnet") != nullptr ? ParseTelnetConfig(object.at("telnet").as_object()) : TelnetConfig{}
			};
		}

		GameConfig ParseGameConfig(const json::object& object)
		{
			return GameConfig{
				ParseNetworkConfig(object.at("innerNetwork").as_object()),
				object.if_contains("telnet") != nullptr ? ParseTelnetConfig(object.at("telnet").as_object()) : TelnetConfig{}
			};
		}
	}

	ClusterConfig LoadClusterConfig(const std::string& configPath)
	{
		std::ifstream stream(configPath);
		if (!stream.is_open())
		{
			throw std::runtime_error("Failed to open config file: " + configPath);
		}

		std::ostringstream buffer;
		buffer << stream.rdbuf();
		const auto jsonValue = json::parse(buffer.str());
		const auto& root = jsonValue.as_object();

		ClusterConfig clusterConfig;
		clusterConfig.env = EnvConfig{
			GetString(root.at("env").as_object(), "id"),
			GetString(root.at("env").as_object(), "environment")
		};
		clusterConfig.logging = LoggingConfig{
			GetString(root.at("logging").as_object(), "rootDir"),
			GetString(root.at("logging").as_object(), "minLevel"),
			GetInt(root.at("logging").as_object(), "flushIntervalMs"),
			GetLoggingEnableFile(root.at("logging").as_object()),
			GetBool(root.at("logging").as_object(), "rotateDaily"),
			GetInt(root.at("logging").as_object(), "maxFileSizeMB"),
			GetInt(root.at("logging").as_object(), "maxRetainedFiles")
		};
		clusterConfig.kcp = KcpConfig{
			GetInt(root.at("kcp").as_object(), "mtu"),
			GetInt(root.at("kcp").as_object(), "sndwnd"),
			GetInt(root.at("kcp").as_object(), "rcvwnd"),
			GetBool(root.at("kcp").as_object(), "nodelay"),
			GetInt(root.at("kcp").as_object(), "intervalMs"),
			GetInt(root.at("kcp").as_object(), "fastResend"),
			GetBool(root.at("kcp").as_object(), "noCongestionWindow"),
			GetInt(root.at("kcp").as_object(), "minRtoMs"),
			GetInt(root.at("kcp").as_object(), "deadLinkCount"),
			GetBool(root.at("kcp").as_object(), "streamMode")
		};
		clusterConfig.managed = ManagedConfig{
			GetString(root.at("managed").as_object(), "frameworkDll"),
			GetString(root.at("managed").as_object(), "gameplayDll")
		};
		clusterConfig.gm = ParseGMConfig(root.at("gm").as_object());

		for (const auto& [serverId, gateConfig] : root.at("gate").as_object())
		{
			clusterConfig.gate.emplace(std::string(serverId), ParseGateConfig(gateConfig.as_object()));
		}

		for (const auto& [serverId, gameConfig] : root.at("game").as_object())
		{
			clusterConfig.game.emplace(std::string(serverId), ParseGameConfig(gameConfig.as_object()));
		}

		return clusterConfig;
	}

	std::string_view GetCanonicalGmServerId()
	{
		return kCanonicalGmServerId;
	}

	bool IsGmServerId(std::string_view serverId)
	{
		return serverId == "gm" || serverId == kCanonicalGmServerId;
	}

	const GateConfig* FindGateConfig(const ClusterConfig& clusterConfig, std::string_view serverId)
	{
		const auto iterator = clusterConfig.gate.find(std::string(serverId));
		return iterator == clusterConfig.gate.end() ? nullptr : &iterator->second;
	}

	const GameConfig* FindGameConfig(const ClusterConfig& clusterConfig, std::string_view serverId)
	{
		const auto iterator = clusterConfig.game.find(std::string(serverId));
		return iterator == clusterConfig.game.end() ? nullptr : &iterator->second;
	}
}
