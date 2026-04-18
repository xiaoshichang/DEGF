#include "ServerBase.h"

namespace de::server::engine
{
	class GateServer : public ServerBase
	{
	public:
		GateServer();
		~GateServer() override;
		void Init() override;
		void Run() override;
		void Uninit() override;
	};
}
