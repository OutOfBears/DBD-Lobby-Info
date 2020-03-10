using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DBD_Magic
{
    public class JsonContent : StringContent
    {
        public JsonContent(object json) : base(JsonConvert.SerializeObject(json), Encoding.UTF8, "application/json")
        {}
    }

    public class ApiClient : HttpClient
    {
        private HttpInternalHandler _handler;

        public ApiClient() : base(new HttpInternalHandler(out HttpInternalHandler handler))
        {
            _handler = handler;
        }

        public async Task<bool> RefreshAuth()
        {
            return await _handler.RefreshAuth(CancellationToken.None);
        }


        internal class HttpInternalHandler : DelegatingHandler
        {

            public HttpInternalHandler(out HttpInternalHandler httpInternalHandler) : base(new HttpClientHandler())
            {
                httpInternalHandler = this;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var retries = 0;
                HttpResponseMessage response = null;

                while (true)
                {
                    retries++;
                    if (retries > 3)
                    {
                        break;
                    }

                    response = await base.SendAsync(request, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.Forbidden
                        && request.RequestUri.Authority == "steam.live.bhvrdbd.com")
                    {
                        if (await RefreshAuth(cancellationToken))
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }


                }

                return response;
            }

            public async Task<bool> RefreshAuth(CancellationToken cancellationToken)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"https://steam.live.bhvrdbd.com/api/v1/auth/login/guest")
                {
                    Content = new JsonContent(new
                    {
                        clientData = new
                        {
                            consentId = "2"
                        }
                    })
                };

                var response = await base.SendAsync(request, cancellationToken);
                return response.StatusCode == HttpStatusCode.OK;
            }
        }
    }
}
