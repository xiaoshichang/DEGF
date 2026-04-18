#include "server/GMServer.h"
#include <memory>

int main() 
{
	auto server = std::make_unique<de::server::engine::GMServer>();
	server->Init();
	server->Run();
	server->Uninit();
    return 0;
}
