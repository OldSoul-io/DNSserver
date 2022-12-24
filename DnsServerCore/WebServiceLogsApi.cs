﻿/*
Technitium DNS Server
Copyright (C) 2022  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using DnsServerCore.ApplicationCommon;
using DnsServerCore.Dns.Applications;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.Dns;
using TechnitiumLibrary.Net.Dns.ResourceRecords;

namespace DnsServerCore
{
    class WebServiceLogsApi
    {
        #region variables

        readonly DnsWebService _dnsWebService;

        #endregion

        #region constructor

        public WebServiceLogsApi(DnsWebService dnsWebService)
        {
            _dnsWebService = dnsWebService;
        }

        #endregion

        #region public

        public void ListLogs(Utf8JsonWriter jsonWriter)
        {
            string[] logFiles = _dnsWebService._log.ListLogFiles();

            Array.Sort(logFiles);
            Array.Reverse(logFiles);

            jsonWriter.WritePropertyName("logFiles");
            jsonWriter.WriteStartArray();

            foreach (string logFile in logFiles)
            {
                jsonWriter.WriteStartObject();

                jsonWriter.WriteString("fileName", Path.GetFileNameWithoutExtension(logFile));
                jsonWriter.WriteString("size", WebUtilities.GetFormattedSize(new FileInfo(logFile).Length));

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
        }

        public Task DownloadLogAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            string strFileName = request.QueryString["fileName"];
            if (string.IsNullOrEmpty(strFileName))
                throw new DnsWebServiceException("Parameter 'fileName' missing.");

            int limit;
            string strLimit = request.QueryString["limit"];
            if (string.IsNullOrEmpty(strLimit))
                limit = 0;
            else
                limit = int.Parse(strLimit);

            return _dnsWebService._log.DownloadLogAsync(request, response, strFileName, limit * 1024 * 1024);
        }

        public void DeleteLog(HttpListenerRequest request)
        {
            string log = request.QueryString["log"];
            if (string.IsNullOrEmpty(log))
                throw new DnsWebServiceException("Parameter 'log' missing.");

            _dnsWebService._log.DeleteLog(log);

            _dnsWebService._log.Write(DnsWebService.GetRequestRemoteEndPoint(request), "[" + _dnsWebService.GetSession(request).User.Username + "] Log file was deleted: " + log);
        }

        public void DeleteAllLogs(HttpListenerRequest request)
        {
            _dnsWebService._log.DeleteAllLogs();

            _dnsWebService._log.Write(DnsWebService.GetRequestRemoteEndPoint(request), "[" + _dnsWebService.GetSession(request).User.Username + "] All log files were deleted.");
        }

        public void DeleteAllStats(HttpListenerRequest request)
        {
            _dnsWebService._dnsServer.StatsManager.DeleteAllStats();

            _dnsWebService._log.Write(DnsWebService.GetRequestRemoteEndPoint(request), "[" + _dnsWebService.GetSession(request).User.Username + "] All stats files were deleted.");
        }

        public async Task QueryLogsAsync(HttpListenerRequest request, Utf8JsonWriter jsonWriter)
        {
            string name = request.QueryString["name"];
            if (string.IsNullOrEmpty(name))
                throw new DnsWebServiceException("Parameter 'name' missing.");

            string classPath = request.QueryString["classPath"];
            if (string.IsNullOrEmpty(classPath))
                throw new DnsWebServiceException("Parameter 'classPath' missing.");

            if (!_dnsWebService._dnsServer.DnsApplicationManager.Applications.TryGetValue(name, out DnsApplication application))
                throw new DnsWebServiceException("DNS application was not found: " + name);

            if (!application.DnsQueryLoggers.TryGetValue(classPath, out IDnsQueryLogger logger))
                throw new DnsWebServiceException("DNS application '" + classPath + "' class path was not found: " + name);

            long pageNumber;
            string strPageNumber = request.QueryString["pageNumber"];
            if (string.IsNullOrEmpty(strPageNumber))
                pageNumber = 1;
            else
                pageNumber = long.Parse(strPageNumber);

            int entriesPerPage;
            string strEntriesPerPage = request.QueryString["entriesPerPage"];
            if (string.IsNullOrEmpty(strEntriesPerPage))
                entriesPerPage = 25;
            else
                entriesPerPage = int.Parse(strEntriesPerPage);

            bool descendingOrder;
            string strDescendingOrder = request.QueryString["descendingOrder"];
            if (string.IsNullOrEmpty(strDescendingOrder))
                descendingOrder = true;
            else
                descendingOrder = bool.Parse(strDescendingOrder);

            DateTime? start;
            string strStart = request.QueryString["start"];
            if (string.IsNullOrEmpty(strStart))
                start = null;
            else
                start = DateTime.Parse(strStart, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            DateTime? end;
            string strEnd = request.QueryString["end"];
            if (string.IsNullOrEmpty(strEnd))
                end = null;
            else
                end = DateTime.Parse(strEnd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            IPAddress clientIpAddress;
            string strClientIpAddress = request.QueryString["clientIpAddress"];
            if (string.IsNullOrEmpty(strClientIpAddress))
                clientIpAddress = null;
            else
                clientIpAddress = IPAddress.Parse(strClientIpAddress);

            DnsTransportProtocol? protocol;
            string strProtocol = request.QueryString["protocol"];
            if (string.IsNullOrEmpty(strProtocol))
                protocol = null;
            else
                protocol = Enum.Parse<DnsTransportProtocol>(strProtocol, true);

            DnsServerResponseType? responseType;
            string strResponseType = request.QueryString["responseType"];
            if (string.IsNullOrEmpty(strResponseType))
                responseType = null;
            else
                responseType = Enum.Parse<DnsServerResponseType>(strResponseType, true);

            DnsResponseCode? rcode;
            string strRcode = request.QueryString["rcode"];
            if (string.IsNullOrEmpty(strRcode))
                rcode = null;
            else
                rcode = Enum.Parse<DnsResponseCode>(strRcode, true);

            string qname = request.QueryString["qname"];
            if (string.IsNullOrEmpty(qname))
                qname = null;

            DnsResourceRecordType? qtype;
            string strQtype = request.QueryString["qtype"];
            if (string.IsNullOrEmpty(strQtype))
                qtype = null;
            else
                qtype = Enum.Parse<DnsResourceRecordType>(strQtype, true);

            DnsClass? qclass;
            string strQclass = request.QueryString["qclass"];
            if (string.IsNullOrEmpty(strQclass))
                qclass = null;
            else
                qclass = Enum.Parse<DnsClass>(strQclass, true);

            DnsLogPage page = await logger.QueryLogsAsync(pageNumber, entriesPerPage, descendingOrder, start, end, clientIpAddress, protocol, responseType, rcode, qname, qtype, qclass);

            jsonWriter.WriteNumber("pageNumber", page.PageNumber);
            jsonWriter.WriteNumber("totalPages", page.TotalPages);
            jsonWriter.WriteNumber("totalEntries", page.TotalEntries);

            jsonWriter.WritePropertyName("entries");
            jsonWriter.WriteStartArray();

            foreach (DnsLogEntry entry in page.Entries)
            {
                jsonWriter.WriteStartObject();

                jsonWriter.WriteNumber("rowNumber", entry.RowNumber);
                jsonWriter.WriteString("timestamp", entry.Timestamp);
                jsonWriter.WriteString("clientIpAddress", entry.ClientIpAddress.ToString());
                jsonWriter.WriteString("protocol", entry.Protocol.ToString());
                jsonWriter.WriteString("responseType", entry.ResponseType.ToString());
                jsonWriter.WriteString("rcode", entry.RCODE.ToString());
                jsonWriter.WriteString("qname", entry.Question?.Name);
                jsonWriter.WriteString("qtype", entry.Question?.Type.ToString());
                jsonWriter.WriteString("qclass", entry.Question?.Class.ToString());
                jsonWriter.WriteString("answer", entry.Answer);

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
        }

        #endregion
    }
}
