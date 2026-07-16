using System;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Helpers
{
    public static class NetworkSafety
    {
        public static HttpClient CreateSafeHttpClient()
        {
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        }

        public static async Task ValidatePublicUriAsync(
            Uri uri,
            bool requireHttps,
            CancellationToken cancellationToken = default)
        {
            if (!uri.IsAbsoluteUri ||
                (uri.Scheme != Uri.UriSchemeHttps && (!(!requireHttps && uri.Scheme == Uri.UriSchemeHttp))))
            {
                throw new InvalidOperationException(requireHttps
                    ? "地址必须使用 HTTPS。"
                    : "地址必须使用 HTTP 或 HTTPS。");
            }

            if (uri.AbsoluteUri.Length > 2048 || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException("地址过长或包含不允许的用户凭据。");
            }

            string host = uri.DnsSafeHost.TrimEnd('.');
            if (string.IsNullOrWhiteSpace(host) ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("不允许访问本机或内部网络主机。");
            }

            IPAddress[] addresses;
            if (IPAddress.TryParse(host, out IPAddress? literal))
            {
                addresses = new[] { literal };
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            }

            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            {
                throw new InvalidOperationException("目标解析到回环、私有、链路本地或保留地址，已阻止请求。");
            }
        }

        public static async Task<HttpResponseMessage> GetFollowingSafeRedirectsAsync(
            HttpClient client,
            Uri initialUri,
            bool requireHttps,
            CancellationToken cancellationToken = default,
            int maxRedirects = 5)
        {
            Uri current = initialUri;
            for (int redirect = 0; redirect <= maxRedirects; redirect++)
            {
                await ValidatePublicUriAsync(current, requireHttps, cancellationToken);
                using HttpRequestMessage request = new(HttpMethod.Get, current);
                HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!IsRedirect(response.StatusCode))
                {
                    return response;
                }

                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location == null)
                {
                    throw new InvalidOperationException("服务器返回了缺少目标地址的重定向。");
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
            }

            throw new InvalidOperationException($"重定向次数超过 {maxRedirects} 次。");
        }

        public static async Task<string> ReadContentAsStringAsync(
            HttpContent content,
            int maxBytes,
            CancellationToken cancellationToken = default)
        {
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
            if (content.Headers.ContentLength is long declaredLength && declaredLength > maxBytes)
            {
                throw new InvalidOperationException($"响应内容超过 {maxBytes / 1024:N0} KB 限制。");
            }

            await using Stream source = await content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new(Math.Min(maxBytes, 64 * 1024));
            byte[] chunk = new byte[16 * 1024];
            int total = 0;
            while (true)
            {
                int read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maxBytes)
                {
                    throw new InvalidOperationException($"响应内容超过 {maxBytes / 1024:N0} KB 限制。");
                }
                buffer.Write(chunk, 0, read);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, total);
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                return b[0] != 0 &&
                       b[0] != 10 &&
                       b[0] != 127 &&
                       !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                       !(b[0] == 169 && b[1] == 254) &&
                       !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                       !(b[0] == 192 && b[1] == 168) &&
                       !(b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) &&
                       !(b[0] == 198 && b[1] is 18 or 19) &&
                       !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
                       !(b[0] == 203 && b[1] == 0 && b[2] == 113) &&
                       b[0] < 224;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] b = address.GetAddressBytes();
                bool documentationRange = b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8;
                return !IPAddress.IPv6Any.Equals(address) &&
                       !IPAddress.IPv6None.Equals(address) &&
                       !IPAddress.IPv6Loopback.Equals(address) &&
                       !address.IsIPv6LinkLocal &&
                       !address.IsIPv6Multicast &&
                       !address.IsIPv6SiteLocal &&
                       (b[0] & 0xfe) != 0xfc &&
                       !documentationRange;
            }

            return false;
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            int value = (int)statusCode;
            return value is 301 or 302 or 303 or 307 or 308;
        }
    }
}
