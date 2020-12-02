# <img src="https://raw.githubusercontent.com/OpenByteDev/Flare.Tcp/master/icon.png" height="40px" /> Flare.Tcp

[![nuget badge](https://badgen.net/nuget/v/Flare.Tcp)](https://www.nuget.org/packages/Flare.Tcp/)
[![Unlicense](https://img.shields.io/github/license/OpenByteDev/Flare.Tcp)](./UNLICENSE)

A basic multi-client message-based tcp server (and client). 

## Using the FlareTcpServer

```csharp
// create a new server and start listening on port 4242
using var server = new FlareTcpServer();
server.Start(4242);

// accept a client and send the string "Test"
using var client = await server.AcceptClientAsync();
await client.WriteMessageAsync(Encoding.UTF8.GetBytes("You connected successfully!"));

// read the response and print it
using var message = await client.ReadNextMessageAsync();
Console.WriteLine(Encoding.UTF8.GetString(message.Span));

// stop listening.
server.Shutdown();
```

## Using the FlareTcpClient

```csharp
// create a new client and connect to localhost on port 4242
using var client = new FlareTcpClient();
await client.ConnectAsync(IPAddress.Loopback, 4242);

// send "Test" to the server and print the response
await client.WriteMessageAsync(Encoding.UTF8.GetBytes("Knock knock"));
using var message = await client.ReadNextMessageAsync();
Console.WriteLine(Encoding.UTF8.GetString(message.Span));

// disconnect from the server
client.Disconnect();
```

## Using the ConcurrentFlareTcpServer

```csharp
// create a new server
using var server = new ConcurrentFlareTcpServer();

// attach event handlers
server.ClientConnected += clientId => {
	Console.WriteLine($"Client {clientId} connected.");
};
server.ClientDisconnected += clientId => {
	Console.WriteLine($"Client {clientId} disconnected.");
	// stop the server
	server.Shutdown();
};
server.MessageReceived += (clientId, message) => {
	// echo message back to client and wait for it to be sent.
	server.EnqueueMessageAndWait(clientId, message.Memory);
	// free the message buffer
	message.Dispose();
};

// start listening on port 4242.
await server.ListenAsync(4242);
```

## Using the ConcurrentFlareTcpClient

```csharp
// create a new client
using var client = new ConcurrentFlareTcpClient();

// attach message callback
client.MessageReceived += message => {
	// print message to console
	Console.WriteLine(Encoding.UTF8.GetString(message.Span));
	// free the message buffer
	message.Dispose();
	// disconnect from server
	client.Disconnect();
};

// connect to localhost on port 4242
await client.ConnectAsync(IPAddress.Loopback, 4242);

// send test message
await client.EnqueueMessageAsync(Encoding.UTF8.GetBytes("Anyone there?"));
```


## Icon

Icon made by <a href="https://www.flaticon.com/authors/smashicons" title="Smashicons">Smashicons</a> from <a href="https://www.flaticon.com/" title="Flaticon"> www.flaticon.com</a>
