#include "ServerBase.h"

namespace de::server::engine
{
	class GameServer : public ServerBase
	{
	public:
		GameServer();
		~GameServer() override;
		void Init() override;
		void Run() override;
		void Uninit() override;
	};
}
