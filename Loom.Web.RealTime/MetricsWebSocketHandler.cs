using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Loom.Web.RealTime
{
    /// <summary>
    /// Zero-alloocation WebSocket handler for streaming metrics.
    /// </summary>
    public sealed class MetricsWebSocketHandler : IDisposable
    {
        private readonly WebSocket _webSocket;
        private readonly ArrayPool<byte> _bufferPool;
        private bool _disposed;

        public MetricsWebSocketHandler(WebSocket webSocket)
        {
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _bufferPool = ArrayPool<byte>.Shared;
        }

        /// <summary>
        /// Stream metrics to the connected WebSocket client.
        /// </summary>
        /// <param name="metricStream"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async ValueTask StreamMetricsAsync(IAsyncEnumerable<MetricUpdate> metricStream, CancellationToken ct = default)
        {
            // Rent a buffer from the pool
            var buffer = _bufferPool.Rent(4096); // 4KB buffer

            try
            {
                // Stream metrics until client disconnects or cancellation
                await foreach(var metric in metricStream.WithCancellation(ct))
                {
                    // Serialize JSON directly into the rented buffer
                    var bytesWritten = SerializeToBuffer(metric, buffer);

                    // Send only the used portion of the buffer
                    await _webSocket.SendAsync(
                        new Memory<byte>(buffer, 0, bytesWritten),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when client disconnects - clean exit
            }
            catch (WebSocketException)
            {
                // Connection lost - clean exit
            }
            finally
            {
                // Return the buffer to the pool
                _bufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Serialize MetricUpdate to JSON bytes using zero-allocation UTF8JsonWriter
        /// </summary>
        /// <param name="metric"></param>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private int SerializeToBuffer(MetricUpdate metric, byte[] buffer)
        {
            var bufferWriter = new RentedArrayBufferWriter(buffer);

            using var writer = new Utf8JsonWriter(bufferWriter);
            JsonSerializer.Serialize(writer, metric, LoomJsonSerializerContext.Default.MetricUpdate);
            writer.Flush();

            return bufferWriter.WrittenCount;
        }

        // Small IBufferWriter<byte> that writes into an existing rented byte[] without allocations.
        private sealed class RentedArrayBufferWriter : IBufferWriter<byte>
        {
            private readonly byte[] _buffer;
            private int _position;

            public RentedArrayBufferWriter(byte[] buffer)
            {
                _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
                _position = 0;
            }

            public void Advance(int count)
            {
                if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
                _position += count;
                if (_position > _buffer.Length) throw new InvalidOperationException("Attempted to write past the end of the buffer.");
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                var remaining = _buffer.Length - _position;
                if (sizeHint > remaining) throw new InvalidOperationException("Insufficient buffer space for the requested sizeHint.");
                return _buffer.AsMemory(_position);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                var remaining = _buffer.Length - _position;
                if (sizeHint > remaining) throw new InvalidOperationException("Insufficient buffer space for the requested sizeHint.");
                return _buffer.AsSpan(_position);
            }

            public int WrittenCount => _position;
        }

        public void Dispose()
        {
            if (!_disposed)
                return;

            if(_webSocket.State == WebSocketState.Open)
            {
                _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Handler disposed",
                    CancellationToken.None
                    ).GetAwaiter().GetResult();
            }

            _webSocket.Dispose();
            _disposed = true;

        }
    }
}
