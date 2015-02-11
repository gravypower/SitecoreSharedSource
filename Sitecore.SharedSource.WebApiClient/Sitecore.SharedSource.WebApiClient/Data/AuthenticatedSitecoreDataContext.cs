﻿using System;
using System.Net;
using System.Text;
using Sitecore.SharedSource.WebApiClient.Interfaces;
using Sitecore.SharedSource.WebApiClient.Net;
using Sitecore.SharedSource.WebApiClient.Util;

namespace Sitecore.SharedSource.WebApiClient.Data
{
    /// <summary>
    /// Represents an authenticated Sitecore data context
    /// </summary>
    public class AuthenticatedSitecoreDataContext : SitecoreDataContext, IAuthenticatedSitecoreDataContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticatedSitecoreDataContext" /> class.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        /// <param name="isSecure">if set to <c>true</c> [is secure].</param>
        /// <param name="credentials">The credentials.</param>
        /// <exception cref="System.ArgumentException">credentials</exception>
        public AuthenticatedSitecoreDataContext(string hostName, ISitecoreCredentials credentials, bool isSecure = false) : base(hostName, isSecure)
        {
            if(isSecure && credentials.EncryptHeaders)
            {
                throw new InvalidOperationException("If you use an SSL connection, the credentials must not be encrypted. The server takes care of header encryption.");
            }

            if (credentials == null)
            {
                throw new ArgumentNullException("credentials", "credentials cannot be null when creating a new instance of AuthenticatedSitecoreDataContext");
            }

            if (!credentials.Validate())
            {
                throw new ArgumentException(credentials.ErrorMessage, "credentials");
            }

            Credentials = credentials;
        }

        #region Implementation of IAuthenticatedSitecoreDataContext

        /// <summary>
        /// Gets the credentials.
        /// </summary>
        /// <value>
        /// The credentials.
        /// </value>
        public ISitecoreCredentials Credentials { get; private set; }

        /// <summary>
        /// Applies the headers.
        /// </summary>
        /// <param name="request">The request.</param>
        public void ApplyHeaders(HttpWebRequest request)
        {
            if (request == null)
                return;

            if (Credentials.EncryptHeaders)
            {
                ApplyEncryptedHeaders(request);
                return;
            }

            request.Headers.Add(Structs.AuthenticationHeaders.UserName, Credentials.UserName);
            request.Headers.Add(Structs.AuthenticationHeaders.Password, Credentials.Password);
        }

        /// <summary>
        /// Applies the encrypted headers.
        /// </summary>
        /// <param name="request">The request.</param>
        public void ApplyEncryptedHeaders(HttpWebRequest request)
        {
            if (request == null)
                return;

            var key = GetPublicKey();

            request.Headers.Add(Structs.AuthenticationHeaders.UserName,
                SecurityUtil.EncryptHeaderValue(Credentials.UserName, key));
            request.Headers.Add(Structs.AuthenticationHeaders.Password,
                SecurityUtil.EncryptHeaderValue(Credentials.Password, key));
            request.Headers.Add(Structs.AuthenticationHeaders.Encrypted, "1");
        }


        /// <summary>
        /// Creates the request.
        /// </summary>
        /// <returns></returns>
        public virtual HttpWebRequest CreateRequest(Uri uri, SitecoreQueryType type, string postData)
        {
            var request = CreateRequest(uri, type);

            byte[] buffer = Encoding.UTF8.GetBytes(postData);

            request.ContentLength = buffer.Length;

            var requestStream = request.GetRequestStream();

            requestStream.Write(buffer, 0, buffer.Length);
            requestStream.Close();
            return request;
        }

        #endregion

        /// <summary>
        /// Creates the request.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public override HttpWebRequest CreateRequest(Uri uri, SitecoreQueryType type)
        {
            var request = base.CreateRequest(uri, type);

            ApplyHeaders(request);

            if (request.Method == "POST" || request.Method == "PUT")
            {
                request.ContentType = "application/x-www-form-urlencoded";
            }

            return request;
        }

        /// <summary>
        /// Gets the response.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query">The query.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException">query</exception>
        public override T GetResponse<T>(IBaseQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            // build the query
            var uri = query.BuildUri(HostName);

            HttpWebRequest request;

            switch (query.QueryType)
            {
                case SitecoreQueryType.Create:
                case SitecoreQueryType.Update:
                    request = CreateRequest(uri, query.QueryType,
                                            ((ISitecoreQuery) query).FieldsToUpdate.ToQueryString());
                    break;
                default:
                    request = CreateRequest(uri, query.QueryType);
                    break;
            }

            // send the request
            return Get(request, query.ResponseFormat, new T());
        }

        /// <summary>
        /// Gets the public key.
        /// </summary>
        /// <returns></returns>
        public override ISitecorePublicKeyResponse GetPublicKey()
        {
            var query = new SitecoreActionQuery("getpublickey");

            // do not authenticate the call to get public key otherwise you will end up in an eternal loop
            // as the authentication routine itself calls GetPublicKey()

            ISitecorePublicKeyResponse response = new SitecoreDataContext(HostName).GetResponse<SitecorePublicKeyResponse>(query);

            return response.Validate() ? response : null;
        }
    }
}
