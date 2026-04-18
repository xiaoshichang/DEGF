#include "ServerBase.h"

namespace de::server::engine
{
	class GMServer : public ServerBase
	{
	public:
		GMServer();
		~GMServer() override;
		void Init() override;
		void Run() override;
		void Uninit() override;
	};
}