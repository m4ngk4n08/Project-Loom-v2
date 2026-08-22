using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;

namespace Loom.Web.RealTime
{
    /// <summary>
    /// Zero-allocation WebSocket handler for streaming metrics.
    /// Not thread-safe: assumes a single StreamMetricsAsync call per handler
    /// instance (one handler per connected socket), since the writer/buffer are
    /// shared instance state reused across frames.
    /// </summary>
    public sealed class MetricsWebSocketHandler : IDisposable
    {
        private readonly WebSocket _webSocket;
        private readonly GrowableRentedBufferWriter _bufferWriter;
        private readonly Utf8JsonWriter _writer;
        private bool _disposed;

        public MetricsWebSocketHandler(WebSocket webSocket)
        {
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _bufferWriter = new GrowableRentedBufferWriter(ArrayPool<byte>.Shared, 4096);
            _writer = new Utf8JsonWriter(_bufferWriter);
        }

        /// <summary>
        /// Stream metrics to the connected WebSocket client.
        /// </summary>
        /// <param name="metricStream"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async ValueTask StreamMetricsAsync(IAsyncEnumerable<MetricUpdate> metricStream, CancellationToken ct = default)
        {
            try
            {
                // Stream metrics until client disconnects or cancellation
                await foreach(var metric in metricStream.WithCancellation(ct))
                {
                    // Reuse the same writer/buffer for every frame - no per-message allocation
                    _bufferWriter.Reset();
                    _writer.Reset(_bufferWriter);
                    JsonSerializer.Serialize(_writer, metric, LoomJsonSerializerContext.Default.MetricUpdate);
                    _writer.Flush();

                    // Send only the used portion of the buffer
                    await _webSocket.SendAsync(
                        _bufferWriter.WrittenMemory,
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
        }

        // IBufferWriter<byte> over a rented byte[] that grows (re-rent + copy) instead of
        // throwing when a frame exceeds the current capacity.
        private sealed class GrowableRentedBufferWriter : IBufferWriter<byte>, IDisposable
        {
            // A single oversized frame (e.g. a large hotpath batch) shouldn't permanently
            // inflate every subsequent frame's buffer. Above this size, shrink back to the
            // initial capacity once the oversized frame has been sent (on the next Reset()).
            private const int ShrinkThreshold = 64 * 1024;

            private readonly ArrayPool<byte> _pool;
            private readonly int _initialSize;
            private byte[] _buffer;
            private int _position;

            public GrowableRentedBufferWriter(ArrayPool<byte> pool, int initialSize)
            {
                _pool = pool;
                _initialSize = initialSize;
                _buffer = pool.Rent(initialSize);
                _position = 0;
            }

            public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _position);

            public void Reset()
            {
                _position = 0;
                if (_buffer.Length > ShrinkThreshold)
                {
                    _pool.Return(_buffer, clearArray: true);
                    _buffer = _pool.Rent(_initialSize);
                }
            }

            public void Advance(int count)
            {
                if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
                if (_position + count > _buffer.Length) throw new InvalidOperationException("Attempted to write past the end of the buffer.");
                _position += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return _buffer.AsMemory(_position);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return _buffer.AsSpan(_position);
            }

            private void EnsureCapacity(int sizeHint)
            {
                var requested = sizeHint > 0 ? sizeHint : 256;
                var remaining = _buffer.Length - _position;
                if (remaining >= requested) return;
                Grow(requested - remaining);
            }

            private void Grow(int additionalNeeded)
            {
                var newSize = Math.Max(_buffer.Length * 2, _buffer.Length + additionalNeeded);
                var newBuffer = _pool.Rent(newSize);
                _buffer.AsSpan(0, _position).CopyTo(newBuffer);
                _pool.Return(_buffer, clearArray: true);
                _buffer = newBuffer;
            }

            public void Dispose()
            {
                _pool.Return(_buffer, clearArray: true);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    // CloseOutputAsync only sends the close frame - it does not block
                    // waiting for the peer's close handshake, which CloseAsync does
                    // (and can hang forever against a vanished client). Bound it with
                    // a short timeout regardless, in case the send itself stalls.
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Handler disposed",
                        cts.Token
                        ).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) when (
                ex is WebSocketException or
                OperationCanceledException or
                IOException or
                ObjectDisposedException)
            {
                // Socket already broken/aborted/timed out - still need to release below
            }
            finally
            {
                _writer.Dispose();
                _bufferWriter.Dispose();
                _webSocket.Dispose();
            }
        }
    }
}
