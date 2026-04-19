# DEGF 项目

本项目是一个游戏开发框架，基于分布式实体(Distributed Entity)的思想而设计。
客户端部分采用Unity作为游戏引擎，逻辑主要用C#开发。
服务器部分采用nethost方式，底层核心逻辑用C++开发，框架逻辑用C#开发。

## 模块划分

### Client\Demo
Client\Demo 为 Unity引擎工程，内部包含客户端C#工程和双端共享工程。
Client\Demo\Assets\Scripts\DE.Client 和 Client\Demo\Assets\Scripts\DE.Client.Foundation 为框架的客户端部分。
Client\Demo\Assets\Scripts\DE.Share 和 Client\Demo\Assets\Scripts\DE.Share.Foundation 为框架的共享部分，代码会被Server\Framework下其他工程引用。
Client\Demo\Demo.sln 可以进行构建或者编译测试。

### Server\Engine
Server\Engine 为 C++ CMake工程，主要实现服务端集群架构的底层模块，例如各类集群节点逻辑，连接方式，控制方式，网络通信，定时服务等。
Server\Engine\build_engine.bat 可以对Engine进行构建，或者测试编译是否正常。
Server\Engine\build_test.bat 可以编译所有单元测试并进行测试。

### Server\Framework
Server\Framework 为 C# 工程，主要包含服务端分布式实体框架的实现。
Server\Framework\Framwwork.sln 包含了所有C#工程，可以进程构建或者编译测试。

### Server\Scripts
Server\Scripts 主要包含服务端重要的流程控制脚本，例如集群启动，集群强杀等。
核心逻辑为python实现，bat或sh进行包装。

## 编码规范
1. 左花括号另起一行
2. 目录名和文件名禁止中文
3. 类名和函数名采用大写驼峰的形式