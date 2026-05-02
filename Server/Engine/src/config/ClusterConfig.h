#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <string_view>
#include <vector>

namespace de::server::engine::config
{
	struct EndpointConfig
	{
		std::string host;
		std::uint16_t port = 0;
	};

	struct NetworkConfig
	{
		EndpointConfig listenEndpoint;
	};

	struct TelnetConfig
	{
		std::uint16_t port = 0;
	};

	struct HttpConfig
	{
		EndpointConfig listenEndpoint{};
	};

	struct EnvConfig
	{
		std::string id;
		std::string environment;
	};

	struct LoggingConfig
	{
		std::string rootDir;
		std::string minLevel;
		int flushIntervalMs = 0;
		bool enableFile = true;
		bool rotateDaily = false;
		int maxFileSizeMB = 0;
		int maxRetainedFiles = 0;
	};

	struct KcpConfig
	{
		int mtu = 0;
		int sndwnd = 0;
		int rcvwnd = 0;
		bool nodelay = false;
		int intervalMs = 0;
		int fastResend = 0;
		bool noCongestionWindow = false;
		int minRtoMs = 0;
		int deadLinkCount = 0;
		bool streamMode = false;
	};

	struct ManagedConfig
	{
		std::string frameworkDll;
		std::string gameplayDll;
	};

	struct GMConfig
	{
		NetworkConfig innerNetwork;
		NetworkConfig controlNetwork;
		HttpConfig http;
		TelnetConfig telnet;
	};

	struct GateConfig
	{
		NetworkConfig innerNetwork;
		NetworkConfig authNetwork;
		NetworkConfig clientNetwork;
		TelnetConfig telnet;
	};

	struct GameConfig
	{
		NetworkConfig innerNetwork;
		TelnetConfig telnet;
	};

	struct ClusterConfig
	{
		EnvConfig env;
		LoggingConfig logging;
		KcpConfig kcp;
		ManagedConfig managed;
		GMConfig gm;
		std::map<std::string, GateConfig> gate;
		std::map<std::string, GameConfig> game;
	};

	ClusterConfig LoadClusterConfig(const std::string& configPath);
	std::string_view GetCanonicalGmServerId();
	bool IsGmServerId(std::string_view serverId);
	const GateConfig* FindGateConfig(const ClusterConfig& clusterConfig, std::string_view serverId);
	const GameConfig* FindGameConfig(const ClusterConfig& clusterConfig, std::string_view serverId);
}
