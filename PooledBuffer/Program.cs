using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Buffer; // Namespace that contains RentedBuffer and ArrayPoolAllocator

namespace BufferUsageDemo
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Demonstrate basic buffer operations
            Console.WriteLine("===== Basic Buffer Operations =====");
            BasicBufferOperations();

            // Network communication example
            Console.WriteLine("\n===== Network Communication Example =====");
            await NetworkCommunicationExample();

            // Performance comparison
            Console.WriteLine("\n===== Performance Comparison =====");
            PerformanceComparison();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static void BasicBufferOperations()
        {
            // Create a writable buffer with capacity of 1024 bytes
            using (var buffer = new RentedBuffer<byte>(1024))
            {
                Console.WriteLine($"Buffer created with capacity: {buffer.MaxCapacity}");
                Console.WriteLine($"Writable bytes: {buffer.WritableBytes}");

                // Write some data to the buffer
                var data = Encoding.UTF8.GetBytes("Hello, RentedBuffer!");
                buffer.Write(data, 0, false); // Do not auto-advance, we'll handle it manually
                Console.WriteLine($"Data written: {data.Length} bytes");
                Console.WriteLine($"Writer index: {buffer.WriterIndex}");
                Console.WriteLine($"Reader index: {buffer.ReaderIndex}");
                Console.WriteLine($"Readable bytes: {buffer.ReadableBytes}");

                // Read data from the buffer
                var readMemory = buffer.ReadMemory(0..buffer.WriterIndex);
                string message = Encoding.UTF8.GetString(readMemory.Span);
                Console.WriteLine($"Read message: {message}");

                // Note: We don't advance here since we haven't moved the writer index yet
                // We can only advance up to WriterIndex - ReaderIndex (ReadableBytes)

                // Reset buffer
                buffer.Reset();
                Console.WriteLine($"After reset, reader index: {buffer.ReaderIndex}");
                Console.WriteLine($"After reset, writer index: {buffer.WriterIndex}");

                // Write new data
                var moreData = Encoding.UTF8.GetBytes("More data after reset!");
                buffer.Write(moreData, 0, false); // Write without auto-advancing
                Console.WriteLine($"More data written: {moreData.Length} bytes");
                Console.WriteLine($"Writer index: {buffer.WriterIndex}");

                // Correctly handle the writer index vs reader index
                // Only advance if there's actual data to consume
                if (buffer.ReadableBytes > 0)
                {
                    int bytesToAdvance = Math.Min(moreData.Length, buffer.ReadableBytes);
                    Console.WriteLine($"Advancing by {bytesToAdvance} bytes");
                    buffer.Advance(bytesToAdvance);
                }
            }
            // Buffer is automatically returned to the pool when disposed
        }

        private static async Task NetworkCommunicationExample()
        {
            try
            {
                // Simple TCP echo server to demonstrate network buffer usage
                var serverTask = StartEchoServerAsync();

                // Give the server some time to start
                await Task.Delay(500);

                // Connect a client and send data
                await SendMessageToEchoServerAsync();

                // Stop the server
                await serverTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Network communication error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private static async Task StartEchoServerAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 8080);
            try
            {
                listener.Start();
                Console.WriteLine("Echo server started on 127.0.0.1:8080");

                // Accept one client connection
                var client = await listener.AcceptTcpClientAsync();
                Console.WriteLine("Client connected");

                using var stream = client.GetStream();

                // Create a buffer for receiving data
                using var receiveBuffer = new RentedBuffer<byte>(4096);

                // Read data from the network
                var networkBuffer = new byte[4096];
                int bytesRead = await stream.ReadAsync(networkBuffer, 0, networkBuffer.Length);

                // Write received data to our buffer
                // For a writable buffer, this automatically updates the writer index
                receiveBuffer.Write(networkBuffer.AsMemory(0, bytesRead), 0, false);
                Console.WriteLine($"Server received: {bytesRead} bytes");

                // Process the data (echo it back)
                // We need to explicitly tell it to read from the start to the writer index
                var dataToSend = receiveBuffer.ReadMemory(0..bytesRead);

                // Send the data back
                await stream.WriteAsync(dataToSend);
                Console.WriteLine($"Server echoed: {dataToSend.Length} bytes");

                // Close the connection
                client.Close();
                Console.WriteLine("Client disconnected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
            finally
            {
                listener.Stop();
                Console.WriteLine("Echo server stopped");
            }
        }

        private static async Task SendMessageToEchoServerAsync()
        {
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, 8080);
                Console.WriteLine("Connected to echo server");

                using var stream = client.GetStream();

                // Create a buffer for the message
                using var sendBuffer = new RentedBuffer<byte>(4096);

                // Prepare message
                string message = "Hello, Echo Server! This is a test message.";
                var messageBytes = Encoding.UTF8.GetBytes(message);

                // Write message to buffer
                sendBuffer.Write(messageBytes, 0, false); // Don't auto-advance
                Console.WriteLine($"Message prepared: '{message}'");

                // Send message from buffer
                var dataToSend = sendBuffer.ReadMemory(0..messageBytes.Length);
                await stream.WriteAsync(dataToSend);
                Console.WriteLine($"Message sent: {dataToSend.Length} bytes");

                // Receive echoed message
                using var receiveBuffer = new RentedBuffer<byte>(4096);
                var networkBuffer = new byte[4096];
                int bytesRead = await stream.ReadAsync(networkBuffer, 0, networkBuffer.Length);

                receiveBuffer.Write(networkBuffer.AsMemory(0, bytesRead), 0, false);

                // Read and process response
                var responseMemory = receiveBuffer.ReadMemory(0..bytesRead);
                string response = Encoding.UTF8.GetString(responseMemory.Span);
                Console.WriteLine($"Response received: '{response}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private static void PerformanceComparison()
        {
            const int iterations = 100000;
            const int bufferSize = 8192;

            // Standard allocation
            Stopwatch standardWatch = new Stopwatch();
            standardWatch.Start();

            for (int i = 0; i < iterations; i++)
            {
                // Create and fill standard byte array
                byte[] standardArray = new byte[bufferSize];
                Array.Fill(standardArray, (byte)1);

                // Use and discard (in real-world, it would be collected by GC)
            }

            standardWatch.Stop();
            Console.WriteLine($"Standard allocation: {standardWatch.ElapsedMilliseconds} ms");

            // RentedBuffer with ArrayPool
            Stopwatch pooledWatch = new Stopwatch();
            pooledWatch.Start();

            for (int i = 0; i < iterations; i++)
            {
                // Create and fill pooled byte array
                using var rentedBuffer = new RentedBuffer<byte>(bufferSize);
                byte[] fillData = new byte[bufferSize];
                Array.Fill(fillData, (byte)1);
                rentedBuffer.Write(fillData, 0, true);

                // Buffer automatically returned to pool on dispose
            }

            pooledWatch.Stop();
            Console.WriteLine($"Pooled allocation: {pooledWatch.ElapsedMilliseconds} ms");

            // Show the difference
            double improvement = (double)standardWatch.ElapsedMilliseconds / pooledWatch.ElapsedMilliseconds;
            Console.WriteLine($"Performance improvement: {improvement:F2}x faster using pooled buffers");

            // Memory pressure demonstration
            Console.WriteLine("\nMemory pressure before GC: {0:N0}", GC.GetTotalMemory(false));
            GC.Collect();
            Console.WriteLine("Memory pressure after GC: {0:N0}", GC.GetTotalMemory(true));
        }
    }

    // Example of a custom network packet processor using RentedBuffer
    public class PacketProcessor
    {
        // Process incoming network packets
        public void ProcessPacket(byte[] rawData, int offset, int length)
        {
            // Create a buffer from the raw packet data
            using var packetBuffer = new RentedBuffer<byte>(rawData.AsMemory(offset, length));

            // Read packet header (example assumes first 4 bytes are packet type)
            int packetType = BitConverter.ToInt32(packetBuffer.ReadMemory(0..4).Span);

            // Advance reader past the header
            packetBuffer.Advance(4);

            // Process the packet based on its type
            switch (packetType)
            {
                case 1: // Authentication packet
                    ProcessAuthPacket(packetBuffer);
                    break;
                case 2: // Data packet
                    ProcessDataPacket(packetBuffer);
                    break;
                case 3: // Control packet
                    ProcessControlPacket(packetBuffer);
                    break;
                default:
                    Console.WriteLine($"Unknown packet type: {packetType}");
                    break;
            }
        }

        private void ProcessAuthPacket(IRentedBuffer<byte> buffer)
        {
            // Example: Read a username and password from the packet
            int usernameLength = BitConverter.ToInt32(buffer.ReadMemory(0..4).Span);
            buffer.Advance(4);

            string username = Encoding.UTF8.GetString(buffer.ReadMemory(0..usernameLength).Span);
            buffer.Advance(usernameLength);

            int passwordLength = BitConverter.ToInt32(buffer.ReadMemory(0..4).Span);
            buffer.Advance(4);

            string password = Encoding.UTF8.GetString(buffer.ReadMemory(0..passwordLength).Span);

            Console.WriteLine($"Auth packet processed. Username: {username}, Password: [hidden]");
        }

        private void ProcessDataPacket(IRentedBuffer<byte> buffer)
        {
            // Example: Read data payload
            int dataLength = buffer.ReadableBytes;
            var data = buffer.ReadMemory(0..dataLength);

            Console.WriteLine($"Data packet processed. {dataLength} bytes of data");
        }

        private void ProcessControlPacket(IRentedBuffer<byte> buffer)
        {
            // Example: Read control commands
            int commandCode = BitConverter.ToInt32(buffer.ReadMemory(0..4).Span);
            buffer.Advance(4);

            Console.WriteLine($"Control packet processed. Command: {commandCode}");
        }
    }

    // Example of a custom network message builder using RentedBuffer
    public class MessageBuilder
    {
        // Create a network message with header and payload
        public byte[] CreateMessage(int messageType, byte[] payload)
        {
            // Total message size: 8 bytes header (4 for type, 4 for length) + payload size
            int totalSize = 8 + payload.Length;

            // Create a buffer for the complete message
            using var messageBuffer = new RentedBuffer<byte>(totalSize);

            // Write message type (4 bytes)
            var typeBytes = BitConverter.GetBytes(messageType);
            messageBuffer.Write(typeBytes, 0, true);

            // Write payload length (4 bytes)
            var lengthBytes = BitConverter.GetBytes(payload.Length);
            messageBuffer.Write(lengthBytes, messageBuffer.ReaderIndex, true);

            // Write payload
            messageBuffer.Write(payload, messageBuffer.ReaderIndex, true);

            // Create the final message bytes
            var messageMemory = messageBuffer.ReadMemory(0..totalSize);
            return messageMemory.ToArray();
        }

        // Create a large streaming message using chunking
        public async Task SendLargeMessageAsync(Stream destination, byte[] payload, int chunkSize = 4096)
        {
            // Calculate number of chunks
            int totalChunks = (payload.Length + chunkSize - 1) / chunkSize;

            for (int i = 0; i < totalChunks; i++)
            {
                // Calculate the size of this chunk
                int currentChunkSize = Math.Min(chunkSize, payload.Length - (i * chunkSize));

                // Create a header for this chunk: chunk index and size
                using var chunkBuffer = new RentedBuffer<byte>(8 + currentChunkSize);

                // Write chunk index (4 bytes)
                var indexBytes = BitConverter.GetBytes(i);
                chunkBuffer.Write(indexBytes, 0, true);

                // Write chunk size (4 bytes)
                var sizeBytes = BitConverter.GetBytes(currentChunkSize);
                chunkBuffer.Write(sizeBytes, chunkBuffer.ReaderIndex, true);

                // Write chunk data
                int offset = i * chunkSize;
                chunkBuffer.Write(payload.AsMemory(offset, currentChunkSize), chunkBuffer.ReaderIndex, true);

                // Send this chunk
                var chunkMemory = chunkBuffer.ReadMemory(0..(8 + currentChunkSize));
                await destination.WriteAsync(chunkMemory);
            }
        }
    }
}