set root=%~dp0

mkdir build
mkdir bin

cd build
cmake ..\azmq-1.0.3 -DBOOST_ROOT=%root%\..\Boost\Bin\Windows64 -DZMQ_ROOT=%root%\..\ZeroMQ\bin\debug
cmake --build . --config Debug
cmake --install . --prefix %root%\bin --config Debug

pause
