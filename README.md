# Basic.Tcp

[![nuget badge](https://badgen.net/nuget/v/Basic.Tcp)](https://www.nuget.org/packages/Basic.Tcp/)
[![Unlicense](https://img.shields.io/github/license/OpenByteDev/Basic.Tcp)](./LICENSE)

A basic asynchronous multi-client event-based TCP messaging server (and client).

## Using the BasicTcpServer

```csharp
// create a new server that listens on port 4269
using var server = new BasicTcpServer(4269);

// attach event handlers
server.ClientConnected += clientId => {
    Console.WriteLine($"Client {clientId} connected.");
};
server.ClientDisconnected += clientId => {
    Console.WriteLine($"Client {clientId} disconnected.");
};
server.MessageReceived += (clientId, message) => {
    // echo message back to client
    server.EnqueueMessage(clientId, message.ToArray());
};

// wait for incoming connections
await server.ListenAsync();
```

## Using the BasicTcpClient

```csharp
// create a new client
using var client = new BasicTcpClient();

// attach message listener
client.MessageReceived += message => {
    // print message to console
    Console.WriteLine(Encoding.UTF8.GetString(message));
};

// connect to localhost
await client.ConnectAsync(IPAddress.Loopback, 4269);

// send test message
await client.SendMessageAsync(Encoding.UTF8.GetBytes("Anyone there?"));

// read next message from server
await client.ReadMessageAsync();

// disconnect
client.Disconnect();
```

