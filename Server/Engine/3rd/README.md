# Third-Party Dependencies

This directory vendors upstream source code directly into the repository. The
project integrates these libraries from local source via `add_subdirectory(...)`
or project-side `INTERFACE` targets and does not download dependencies during
CMake configure or build.

## Current Pins

| Directory | Upstream | Version | Notes |
| --- | --- | --- | --- |
| `Boost/` | https://www.boost.org/ | `1.85.0` | Unified native dependency pack. Engine logging uses `Boost.Log`, JSON parsing uses `Boost.JSON`, and networking/event-loop support uses `Boost.Asio` plus required transitive modules from the local prebuilt package. |
| `zeromq/` | https://github.com/zeromq/libzmq | `v4.3.5` | ZeroMQ core library used for node-to-node TCP messaging. |
| `kcp/` | https://github.com/skywind3000/kcp | `master@f4f3a89cc632647dabdcb146932d2afd5591e62e` | Upstream `ikcp.c` / `ikcp.h` snapshot used for Gate-side client KCP sessions and future UDP listener integration. |
| `dotnet_host/` | https://www.nuget.org/packages/Microsoft.NETCore.App.Host.win-x64 and https://www.nuget.org/packages/Microsoft.NETCore.App.Host.linux-x64 | `10.0.0` | Official .NET native hosting headers plus the platform `nethost` link artifacts used for CLR bootstrap on Windows/Linux (`nethost.lib` + `nethost.dll` on Windows, `libnethost.a` on Linux). |

## Build Integration

- Root `CMakeLists.txt` includes `cmake/SetupBoostLibrary.cmake` first so CMake
  can resolve the locally prepared Boost package from `3rd/Boost`.
- `3rd/CMakeLists.txt` maps project-side `INTERFACE` aliases to
  `Boost::headers`, `Boost::json`, `Boost::log`, `Boost::log_setup`, and
  `Boost::system`, while continuing to build vendored `zeromq` and `kcp`
  from local source.
- `3rd/CMakeLists.txt` also exposes vendored .NET hosting headers and the
  platform `nethost` link artifact through a project-side `INTERFACE` target.
- Internal aliases `de::thirdparty::boost_asio`, `de::thirdparty::boost_json`,
  `de::thirdparty::boost_log`, `de::thirdparty::zeromq`,
  `de::thirdparty::kcp`, and `de::thirdparty::nethost` shield project code
  from upstream target naming or directory layout details.
- Compatibility aliases `de::thirdparty::asio`, `de::thirdparty::nlohmann_json`,
  and `de::thirdparty::spdlog` currently remain available but now resolve to
  Boost-backed targets.
- `de::thirdparty::nethost` links the vendored host artifact and adds the
  platform integration required by the official hosting API (`dl` on Linux and
  `nethost.dll` deployment on Windows).
