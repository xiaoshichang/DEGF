#include "config/ClusterConfig.h"

#include <fstream>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace de::server::engine::config
{
	namespace
	{
		EndpointConfig ParseEndpointConfig(const nlohmann::json& json)
		{
			return EndpointConfig{
				json.at("host").get<std::string>(),
				json.at("port").get<std::uint16_t>()
			};
		}

		NetworkConfig ParseNetworkConfig(const nlohmann::json& json)
		{
			return NetworkConfig{
				ParseEndpointConfig(json.at("listenEndpoint"))
			};
		}

		TelnetConfig ParseTelnetConfig(const nlohmann::json& json)
		{
			return TelnetConfig{
				json.value("port", static_cast<std::uint16_t>(0))
			};
		}

		GMConfig ParseGMConfig(const nlohmann::json& json)
		{
			return GMConfig{
				ParseNetworkConfig(json.at("innerNetwork")),
				ParseNetworkConfig(json.at("controlNetwork")),
				json.contains("telnet") ? ParseTelnetConfig(json.at("telnet")) : TelnetConfig{}
			};
		}

		GateConfig ParseGateConfig(const nlohmann::json& json)
		{
			return GateConfig{
				ParseNetworkConfig(json.at("innerNetwork")),
				ParseNetworkConfig(json.at("authNetwork")),
				ParseNetworkConfig(json.at("clientNetwork")),
				json.contains("telnet") ? ParseTelnetConfig(json.at("telnet")) : TelnetConfig{}
			};
		}

		GameConfig ParseGameConfig(const nlohmann::json& json)
		{
			return GameConfig{
				ParseNetworkConfig(json.at("innerNetwork")),
				json.contains("telnet") ? ParseTelnetConfig(json.at("telnet")) : TelnetConfig{}
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

		nlohmann::json json;
		stream >> json;

		ClusterConfig clusterConfig;
		clusterConfig.env = EnvConfig{
			json.at("env").at("id").get<std::string>(),
			json.at("env").at("environment").get<std::string>()
		};
		clusterConfig.logging = LoggingConfig{
			json.at("logging").at("rootDir").get<std::string>(),
			json.at("logging").at("minLevel").get<std::string>(),
			json.at("logging").at("flushIntervalMs").get<int>(),
			json.at("logging").at("enableConsole").get<bool>(),
			json.at("logging").at("rotateDaily").get<bool>(),
			json.at("logging").at("maxFileSizeMB").get<int>(),
			json.at("logging").at("maxRetainedFiles").get<int>()
		};
		clusterConfig.kcp = KcpConfig{
			json.at("kcp").at("mtu").get<int>(),
			json.at("kcp").at("sndwnd").get<int>(),
			json.at("kcp").at("rcvwnd").get<int>(),
			json.at("kcp").at("nodelay").get<bool>(),
			json.at("kcp").at("intervalMs").get<int>(),
			json.at("kcp").at("fastResend").get<int>(),
			json.at("kcp").at("noCongestionWindow").get<bool>(),
			json.at("kcp").at("minRtoMs").get<int>(),
			json.at("kcp").at("deadLinkCount").get<int>(),
			json.at("kcp").at("streamMode").get<bool>()
		};
		clusterConfig.managed = ManagedConfig{
			json.at("managed").at("assemblyName").get<std::string>(),
			json.at("managed").at("assemblyPath").get<std::string>(),
			json.at("managed").at("runtimeConfigPath").get<std::string>(),
			json.at("managed").at("searchAssemblyPaths").get<std::vector<std::string>>()
		};
		clusterConfig.gm = ParseGMConfig(json.at("gm"));

		for (const auto& [serverId, gateConfig] : json.at("gate").items())
		{
			clusterConfig.gate.emplace(serverId, ParseGateConfig(gateConfig));
		}

		for (const auto& [serverId, gameConfig] : json.at("game").items())
		{
			clusterConfig.game.emplace(serverId, ParseGameConfig(gameConfig));
		}

		return clusterConfig;
	}

	bool IsGmServerId(std::string_view serverId)
	{
		return serverId == "gm" || serverId == "GM";
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
