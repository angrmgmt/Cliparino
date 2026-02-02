/*  Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
    Copyright (C) 2024 Scott Mongrain - (angrmgmt@gmail.com)

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301
    USA
*/

#region

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

#endregion

/// <summary>
///     Provides utilities for building HTTP responses with consistent headers and formatting.
/// </summary>
public static class HttpResponseBuilder {
    /// <summary>
    ///     Builds a complete HTTP response with the specified content, headers, and status code.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <param name="content">The response content.</param>
    /// <param name="contentType">The MIME type of the content.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="additionalHeaders">Additional headers to include.</param>
    /// <param name="nonce">Optional nonce for security headers.</param>
    public static void BuildResponse(
        HttpListenerContext context,
        string content,
        string contentType = "text/html",
        int statusCode = 200,
        Dictionary<string, string> additionalHeaders = null,
        string nonce = null) {
        
        try {
            var response = context.Response;
            
            // Set status code
            response.StatusCode = statusCode;
            
            // Set content type
            response.ContentType = contentType;
            
            // Add standard headers
            AddStandardHeaders(response, nonce);
            
            // Add additional headers if provided
            if (additionalHeaders != null) {
                foreach (var header in additionalHeaders) {
                    response.Headers.Add(header.Key, header.Value);
                }
            }
            
            // Write content
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            
            response.Close();
        } catch (Exception) {
            // Log error but don't throw to avoid breaking the HTTP listener
            try {
                context.Response.StatusCode = 500;
                context.Response.Close();
            } catch {
                // Ignore errors during error handling
            }
        }
    }

    /// <summary>
    ///     Builds a JSON response with proper content type and headers.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <param name="jsonContent">The JSON content to send.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="nonce">Optional nonce for security headers.</param>
    public static void BuildJsonResponse(
        HttpListenerContext context,
        string jsonContent,
        int statusCode = 200,
        string nonce = null) {
        
        BuildResponse(context, jsonContent, "application/json", statusCode, null, nonce);
    }

    /// <summary>
    ///     Builds an error response with a standard error message.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="errorMessage">The error message to display.</param>
    /// <param name="logger">Optional logger for error reporting.</param>
    public static void BuildErrorResponse(
        HttpListenerContext context,
        int statusCode,
        string errorMessage,
        CPHLogger logger = null) {
        
        logger?.Log(LogLevel.Error, $"HTTP {statusCode}: {errorMessage}");
        
        var errorContent = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Error {statusCode}</title>
            <style>
                body {{ font-family: Arial, sans-serif; margin: 40px; }}
                .error {{ color: #d32f2f; }}
                .code {{ font-family: monospace; background: #f5f5f5; padding: 2px 4px; }}
            </style>
        </head>
        <body>
            <h1 class=""error"">Error {statusCode}</h1>
            <p>{errorMessage}</p>
            <p><small>Cliparino HTTP Server</small></p>
        </body>
        </html>";
        
        BuildResponse(context, errorContent, "text/html", statusCode);
    }

    /// <summary>
    ///     Adds standard headers to the HTTP response.
    /// </summary>
    /// <param name="response">The HTTP response object.</param>
    /// <param name="nonce">Optional nonce for security headers.</param>
    private static void AddStandardHeaders(HttpListenerResponse response, string nonce = null) {
        // CORS headers
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "*");
        
        // Cache control headers
        response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        response.Headers.Add("Pragma", "no-cache");
        response.Headers.Add("Expires", "0");
        
        // Security headers
        if (!string.IsNullOrEmpty(nonce)) {
            response.Headers.Add("Content-Security-Policy", 
                $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;");
        }
    }

    /// <summary>
    ///     Generates a random nonce for security headers.
    /// </summary>
    /// <param name="length">The length of the nonce.</param>
    /// <returns>A random nonce string.</returns>
    public static string GenerateNonce(int length = CliparinoConstants.Http.NonceLength) {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var result = new char[length];
        
        for (var i = 0; i < length; i++) {
            result[i] = chars[random.Next(chars.Length)];
        }
        
        return new string(result);
    }
}