

namespace de::server::engine
{
	class ServerBase
	{
	public:
		virtual ~ServerBase() = default;
		virtual void Init() = 0;
		virtual void Run() = 0;
		virtual void Uninit() = 0;
	};
}