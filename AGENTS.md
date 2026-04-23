# DEGF 项目

本项目是一个游戏开发框架，基于分布式实体(Distributed Entity)的思想而设计。
客户端部分采用Unity作为游戏引擎，逻辑主要用C#开发。
服务器部分采用nethost方式，底层核心逻辑用C++开发，框架逻辑用C#开发。

## 架构设计
### 服务器集群架构
每一个服务器集群包含一个GM节点，若干Game节点和若干Gate节点。
1. GM节点的作用是控制集群整体行为，包括启动、关闭、性能统计、GM指令分发等。
2. Game节点的作用是承载游戏业务逻辑，这些逻辑主要在Game上的nethost中以C#实现。Game之间不直连，通信通过Gate转发。
3. Gate节点的作用是客户端账号鉴权，客户端连接管理，转发Game的rpc消息等。

### 分布式实体架构
Entity是一切实体的基类，包括一个Guid作为唯一ID。
Entity上能描述属性，需要进行序列化和反序列化。
Entity在客户端和服务器派生出ClientEntity和ServerEntity。

ServerEntity分为可迁移和不可迁移两种。
目前可迁移的ServerEntity只有一种，就是代表玩家角色的AvatarEntity。
代表同一个角色的AvatarEntity在客户端和服务器一一对应，可以通过属性同步和RPC进行通信。
不可迁移的ServerEntity很多，常见的包括NPC、Space等。
一个服务器集群范围内，有大量全局唯一服务，称为 StubEntity。例如 OnlineStub、MatchStub、SpaceStub等。


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