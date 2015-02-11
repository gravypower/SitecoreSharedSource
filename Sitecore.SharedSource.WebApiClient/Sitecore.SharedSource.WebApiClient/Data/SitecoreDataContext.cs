using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Sitecore.SharedSource.WebApiClient.Interfaces;
using Sitecore.SharedSource.WebApiClient.Net;
using Sitecore.SharedSource.WebApiClient.Util;

namespace Sitecore.SharedSource.WebApiClient.Data
{
    /// <summary>
    /// Represents an un-authenticated Sitecore data context
    /// </summary>
    public class SitecoreDataContext : ISitecoreDataContext
    {
        private string _hostName;

        /// <summary>
        /// Initializes a new instance of the <see cref="SitecoreDataContext" /> class.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        /// <param name="isSecure">if set to <c>true</c> [is secure].</param>
        /// <exception cref="System.ArgumentException">hostName cannot be null, empty or an un-recognized type</exception>
        /// <exception cref="System.NotSupportedException">the hostName paramater cannot be null or empty when creating an instance of SitecoreDataContext</exception>
        public  SitecoreDataContext(string hostName, bool isSecure = false)
        {
            var hostNameType = Uri.CheckHostName(hostName.Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/'));

            if (hostNameType == UriHostNameType.Unknown)
            {
                throw new ArgumentException("hostName cannot be null, empty or an un-recognized type");
            }

            _hostName = isSecure ? hostName.EnsurePrefix("https://") : hostName.EnsurePrefix("http://");
        }

        #region Implementation of ISitecoreDataContext

        /// <summary>
        /// Gets the response.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query">The query.</param>
        /// <returns></returns>
        public virtual T GetResponse<T>(IBaseQuery query) where T: class, IBaseResponse, new()
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            if (query.QueryType == SitecoreQueryType.Create || query.QueryType == SitecoreQueryType.Update)
            {
                throw new InvalidOperationException("A create or update query must be used with an authenticated data context");
            }

            // build the query
            var uri = query.BuildUri(_hostName);

            var request = CreateRequest(uri, query.QueryType);

            // send the request
            return Get(request, query.ResponseFormat, new T());
        }

        /// <summary>
        /// Gets the specified URI.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request">The request.</param>
        /// <param name="responseFormat">The response format.</param>
        /// <param name="scResponse">The sc response.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">scResponse</exception>
        public virtual T Get<T>(HttpWebRequest request, ResponseFormat responseFormat, T scResponse) where T: class, IBaseResponse
        {
            if (scResponse == null)
                throw new ArgumentNullException("scResponse");

            try
            {
                var sw = Stopwatch.StartNew();

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    var stream = response.GetResponseStream();

                    sw.Stop();

                    if (stream != null)
                    {
                        var sReader = new StreamReader(stream);

                      
                            switch (responseFormat)
                            {
                                case ResponseFormat.Json:
                                    scResponse = DeserializeJsonResponse<T>(sReader.ReadToEnd());
                                    break;
                                case ResponseFormat.Xml:
                                    scResponse = DeserializeXmlResponse<T>(sReader.ReadToEnd());
                                    break;
                            }

                           
                                scResponse.Info = new SitecoreWebResponseInfo
                                                      {
                                                          Uri = request.RequestUri,
                                                          ResponseTime = sw.Elapsed
                                                      };

                                scResponse.StatusDescription = response.StatusDescription;

                                
                           
                    }
                }
            }
            catch(WebException ex)
            {
                SetExceptionMetaData(ex, scResponse);

                if (ex.Response != null)
                {
                    var response = (HttpWebResponse) ex.Response;

                    if (scResponse != null)
                    {
                        scResponse.StatusCode = response.StatusCode;
                        scResponse.StatusDescription = response.StatusDescription;
                    }
                }
                else
                {
                    if (scResponse != null)
                    {
                        scResponse.StatusCode = HttpStatusCode.InternalServerError;
                    }
                }

            }
            catch (Exception ex)
            {
                SetExceptionMetaData(ex, scResponse);

                if (scResponse != null)
                {
                    scResponse.StatusCode = HttpStatusCode.InternalServerError;
                }
            }

            return scResponse;
        }

        /// <summary>
        /// Sets the exception meta data.
        /// </summary>
        /// <param name="ex">The ex.</param>
        /// <param name="scResponse">The sc response.</param>
        protected virtual void SetExceptionMetaData(Exception ex, IBaseResponse scResponse)
        {
            if (ex == null)
                return;

            if (scResponse.Info == null)
            {
                scResponse.Info = new SitecoreWebResponseInfo();
            }

            scResponse.Info.ErrorMessage = ex.Message;
            scResponse.Info.StackTrace = ex.StackTrace;
        }

        /// <summary>
        /// Gets or sets the name of the host.
        /// </summary>
        /// <value>
        /// The name of the host.
        /// </value>
        public string HostName
        {
            get { return _hostName; }
            set { _hostName = value; }
        }

        /// <summary>
        /// Creates the request.
        /// </summary>
        /// <returns></returns>
        public virtual HttpWebRequest CreateRequest(Uri uri, SitecoreQueryType type)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);

            request.Method = type.ToHttpMethod();
            request.KeepAlive = false;

            return request;
        }

        /// <summary>
        /// Des the serialize response.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns></returns>
        public virtual T DeserializeJsonResponse<T>(string response) where T: IBaseResponse
        {
            if (string.IsNullOrWhiteSpace(response))
                return default(T);

            return JsonConvert.DeserializeObject<T>(
                                    response,
                                    new JsonSerializerSettings
                                    {
                                        NullValueHandling = NullValueHandling.Ignore,
                                    });
        }

        /// <summary>
        /// Deserializes the XML response.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="response">The response.</param>
        /// <returns></returns>
        public virtual T DeserializeXmlResponse<T>(string response) where T : IBaseResponse
        {
            if (string.IsNullOrWhiteSpace(response))
                return default(T);

            var serializer = new XmlSerializer(typeof(T));

            T result;

            using (TextReader reader = new StringReader(response))
            {
                result = (T)serializer.Deserialize(reader);
            }

            return result;
        }

        /// <summary>
        /// Gets the public key.
        /// </summary>
        /// <returns></returns>
        public virtual ISitecorePublicKeyResponse GetPublicKey()
        {
            var query = new SitecoreActionQuery("getpublickey");

            ISitecorePublicKeyResponse response = GetResponse<SitecorePublicKeyResponse>(query);

            return response.Validate() ? response : null;
        }

        #endregion
    }
}
