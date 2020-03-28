using Aliyun.Api;
using Aliyun.Api.DNS.DNS20150109.Request;
using Aliyun.Api.Domain;
using aliyun_ddns.log;
using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Threading;

namespace aliyun_ddns
{
    class Program
    {
        // 密钥 ID
        private static readonly string ACCESS_KEY_ID = ConfigurationManager.AppSettings["ACCESS_KEY_ID"];
        // 密钥
        private static readonly string ACCESS_KEY_SECRET = ConfigurationManager.AppSettings["ACCESS_KEY_SECRET"];
        // 主域名
        private static readonly string DOMAIN_NAME = ConfigurationManager.AppSettings["DOMAIN_NAME"];
        // 子域名
        private static readonly string SUBDOMAIN_NAME = ConfigurationManager.AppSettings["SUBDOMAIN_NAME"];

        // 本地DNS主机地址, 主要获取局域网IP网段
        private static readonly string LOCAL_DNS_ADDRESS = ConfigurationManager.AppSettings["LOCAL_DNS_ADDRESS"];
        // 运行时出现错误重试的次数
        private static readonly int ERROR_RETRIES = int.Parse(ConfigurationManager.AppSettings["ERROR_RETRIES"]);

        // 阿里云DNS主机地址
        private static readonly string HOST_URL = "https://dns.aliyuncs.com/";

        // 定义最小时间单元: 1分钟
        private static readonly int TIME_UNIT = 60000;

        // 获取本机IP地址
        private static string GetLocalIP(string dnsAddress)
        {
            int errorTimes = 0;
            while (true)
            {
                LogHelper.WriteLog("预定本地DNS主机地址：" + dnsAddress);
                LogHelper.WriteLog("尝试匹配本机IP地址");
                string ipPattern = dnsAddress.Substring(0, dnsAddress.LastIndexOf(".") + 1);
                string hostName = Dns.GetHostName();
                IPAddress[] addressList = Dns.GetHostAddresses(hostName);
                foreach (IPAddress ipAddr in addressList)
                {
                    string address = ipAddr.ToString();
                    if (address.StartsWith(ipPattern))
                    {
                        LogHelper.WriteLog("成功获取本机IP地址：" + address);
                        return address;
                    }
                }
                errorTimes++;
                if (errorTimes > ERROR_RETRIES)
                {
                    LogHelper.WriteLog("多次获取本地IP地址失败，程序退出");
                    Environment.Exit(0);
                }
                else
                {
                    LogHelper.WriteLog("匹配本机IP地址失败，正在尝试重新获取");
                }
                Thread.Sleep(TIME_UNIT);
            }
        }

        // 获取阿里云域名解析记录
        private static Record GetDomainRecord()
        {
            int errorTimes = 0;
            string errMessage;
            while (true)
            {
                var aliyunClient = new DefaultAliyunClient(HOST_URL, ACCESS_KEY_ID, ACCESS_KEY_SECRET);
                var req = new DescribeDomainRecordsRequest() { DomainName = DOMAIN_NAME };
                try
                {
                    var response = aliyunClient.Execute(req);
                    Record record = response.DomainRecords.FirstOrDefault(rec => rec.RR == SUBDOMAIN_NAME && rec.Type == "A");
                    if (record != null && !string.IsNullOrWhiteSpace(record.Value))
                    {
                        LogHelper.WriteLog("获取阿里云域名解析记录为：" + record.Value);
                        return record;
                    }
                    errMessage = "域名解析查询结果为空（可能密钥错误），正在尝试重新获取";
                }
                catch
                {
                    errMessage = "网络连接错误，未能请求到阿里云域名解析服务，正在尝试重新连接";

                }
                errorTimes++;
                if (errorTimes > ERROR_RETRIES)
                {
                    LogHelper.WriteLog("多次请求阿里云域名解析服务失败，程序退出");
                    Environment.Exit(0);
                }
                else
                {
                    LogHelper.WriteLog(errMessage);
                }
                Thread.Sleep(TIME_UNIT);
            }
        }

        // 更新IP信息
        private static void UpdateIP(DefaultAliyunClient aliyunClient, Record record, string ip)
        {
            int errorTimes = 0;
            while (true)
            {
                var changeValueRequest = new UpdateDomainRecordRequest()
                {
                    RecordId = record.RecordId,
                    Value = ip,
                    Type = "A",
                    RR = SUBDOMAIN_NAME
                };
                try
                {
                    aliyunClient.Execute(changeValueRequest);
                    LogHelper.WriteLog(string.Format("域名解析记录更新成功：{0}.{1} => {2}", SUBDOMAIN_NAME, DOMAIN_NAME, ip));
                    return;
                }
                catch
                {
                    errorTimes++;
                    if (errorTimes > ERROR_RETRIES)
                    {
                        LogHelper.WriteLog("多次更新域名解析记录失败，程序退出");
                        Environment.Exit(0);
                    }
                    else
                    {
                        LogHelper.WriteLog("更新域名解析记录失败，正在重试");
                    }
                    Thread.Sleep(TIME_UNIT);
                }
            }
        }

        static void Main(string[] args)
        {
            LogHelper.WriteLog("程序启动");

            // 创建阿里云客户端操作对象
            DefaultAliyunClient aliyunClient = new DefaultAliyunClient(HOST_URL, ACCESS_KEY_ID, ACCESS_KEY_SECRET);

            // 获取本机IP地址
            string localIP = GetLocalIP(LOCAL_DNS_ADDRESS);

            // 获取阿里云解析的IP记录值
            Record record = GetDomainRecord();

            if (localIP != record.Value)
            {
                LogHelper.WriteLog("本地和远端IP记录不同，尝试更新解析记录");
                UpdateIP(aliyunClient, record, localIP);
            }
            else
            {
                LogHelper.WriteLog("本地和远端IP记录相同，本次检测无需更新");
            }
        }
    }
}
