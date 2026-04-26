using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Assets.Scripts.DE.Client.Network
{
    public readonly struct HttpRequestHandle : IEquatable<HttpRequestHandle>
    {
        public HttpRequestHandle(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public bool IsValid => Value > 0;

        public bool Equals(HttpRequestHandle other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is HttpRequestHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(HttpRequestHandle left, HttpRequestHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(HttpRequestHandle left, HttpRequestHandle right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class HttpRequestOptions
    {
        public string Url { get; set; }

        public string Method { get; set; } = UnityWebRequest.kHttpVerbGET;

        public Dictionary<string, string> Headers { get; set; }

        public byte[] Body { get; set; }

        public string ContentType { get; set; }

        public int TimeoutSeconds { get; set; }
    }

    public sealed class HttpResponse
    {
        public HttpRequestHandle Handle { get; set; }

        public bool IsSuccess { get; set; }

        public long StatusCode { get; set; }

        public string Error { get; set; }

        public string Text { get; set; }

        public byte[] Data { get; set; }

        public Dictionary<string, string> Headers { get; set; }
    }

    public static class HttpRequest
    {
        private static readonly object s_syncRoot = new object();
        private static readonly Dictionary<HttpRequestHandle, TaskCompletionSource<HttpResponse>> s_responseTasks =
            new Dictionary<HttpRequestHandle, TaskCompletionSource<HttpResponse>>();
        private static long s_nextHandleValue;

        public static HttpRequestHandle SendAsync(HttpRequestOptions requestOptions, Action<HttpResponse> callback = null)
        {
            if (requestOptions == null)
            {
                throw new ArgumentNullException(nameof(requestOptions));
            }

            if (string.IsNullOrWhiteSpace(requestOptions.Url))
            {
                throw new ArgumentException("Request url is required.", nameof(requestOptions));
            }

            var handle = new HttpRequestHandle(Interlocked.Increment(ref s_nextHandleValue));
            var responseTaskSource = new TaskCompletionSource<HttpResponse>();

            lock (s_syncRoot)
            {
                s_responseTasks[handle] = responseTaskSource;
            }

            UnityWebRequest unityWebRequest = null;

            try
            {
                unityWebRequest = _CreateUnityWebRequest(requestOptions);
                var asyncOperation = unityWebRequest.SendWebRequest();
                asyncOperation.completed += _ =>
                {
                    var response = _BuildResponse(handle, unityWebRequest);
                    responseTaskSource.TrySetResult(response);

                    if (callback != null)
                    {
                        try
                        {
                            callback(response);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                    }

                    unityWebRequest.Dispose();
                };
            }
            catch (Exception exception)
            {
                if (unityWebRequest != null)
                {
                    unityWebRequest.Dispose();
                }

                var response = new HttpResponse
                {
                    Handle = handle,
                    IsSuccess = false,
                    StatusCode = 0,
                    Error = exception.Message,
                    Text = string.Empty,
                    Data = Array.Empty<byte>(),
                    Headers = new Dictionary<string, string>(),
                };

                responseTaskSource.TrySetException(exception);
                if (callback != null)
                {
                    try
                    {
                        callback(response);
                    }
                    catch (Exception callbackException)
                    {
                        Debug.LogException(callbackException);
                    }
                }
            }

            return handle;
        }

        public static HttpRequestHandle GetAsync(
            string url,
            Action<HttpResponse> callback = null,
            Dictionary<string, string> headers = null,
            int timeoutSeconds = 0)
        {
            return SendAsync(
                new HttpRequestOptions
                {
                    Url = url,
                    Method = UnityWebRequest.kHttpVerbGET,
                    Headers = headers,
                    TimeoutSeconds = timeoutSeconds,
                },
                callback);
        }

        public static HttpRequestHandle PostAsync(
            string url,
            string body,
            Action<HttpResponse> callback = null,
            Dictionary<string, string> headers = null,
            string contentType = "application/json",
            int timeoutSeconds = 0)
        {
            var bodyBytes = string.IsNullOrEmpty(body)
                ? Array.Empty<byte>()
                : System.Text.Encoding.UTF8.GetBytes(body);

            return SendAsync(
                new HttpRequestOptions
                {
                    Url = url,
                    Method = UnityWebRequest.kHttpVerbPOST,
                    Headers = headers,
                    Body = bodyBytes,
                    ContentType = contentType,
                    TimeoutSeconds = timeoutSeconds,
                },
                callback);
        }

        public static Task<HttpResponse> GetTask(HttpRequestHandle handle)
        {
            lock (s_syncRoot)
            {
                if (!s_responseTasks.TryGetValue(handle, out var responseTaskSource))
                {
                    throw new KeyNotFoundException("Http request handle not found: " + handle);
                }

                return responseTaskSource.Task;
            }
        }

        public static bool TryGetTask(HttpRequestHandle handle, out Task<HttpResponse> task)
        {
            lock (s_syncRoot)
            {
                if (!s_responseTasks.TryGetValue(handle, out var responseTaskSource))
                {
                    task = null;
                    return false;
                }

                task = responseTaskSource.Task;
                return true;
            }
        }

        private static UnityWebRequest _CreateUnityWebRequest(HttpRequestOptions requestOptions)
        {
            var method = string.IsNullOrWhiteSpace(requestOptions.Method)
                ? UnityWebRequest.kHttpVerbGET
                : requestOptions.Method;
            var unityWebRequest = new UnityWebRequest(requestOptions.Url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
            };

            if (requestOptions.TimeoutSeconds > 0)
            {
                unityWebRequest.timeout = requestOptions.TimeoutSeconds;
            }

            if (requestOptions.Body != null && requestOptions.Body.Length > 0)
            {
                unityWebRequest.uploadHandler = new UploadHandlerRaw(requestOptions.Body);
            }

            if (!string.IsNullOrWhiteSpace(requestOptions.ContentType))
            {
                unityWebRequest.SetRequestHeader("Content-Type", requestOptions.ContentType);
            }

            if (requestOptions.Headers != null)
            {
                foreach (var pair in requestOptions.Headers)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                    {
                        continue;
                    }

                    unityWebRequest.SetRequestHeader(pair.Key, pair.Value ?? string.Empty);
                }
            }

            return unityWebRequest;
        }

        private static HttpResponse _BuildResponse(HttpRequestHandle handle, UnityWebRequest unityWebRequest)
        {
            return new HttpResponse
            {
                Handle = handle,
                IsSuccess = unityWebRequest.result == UnityWebRequest.Result.Success,
                StatusCode = unityWebRequest.responseCode,
                Error = unityWebRequest.error,
                Text = unityWebRequest.downloadHandler == null
                    ? string.Empty
                    : unityWebRequest.downloadHandler.text,
                Data = unityWebRequest.downloadHandler == null
                    ? Array.Empty<byte>()
                    : unityWebRequest.downloadHandler.data,
                Headers = unityWebRequest.GetResponseHeaders() ?? new Dictionary<string, string>(),
            };
        }
    }
}
