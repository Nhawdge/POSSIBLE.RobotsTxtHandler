﻿using System;
using System.Linq;
using System.Text;
using System.Web;
using EPiServer;
using EPiServer.Data.Dynamic;
using EPiServer.Framework.Configuration;
using log4net;

namespace POSSIBLE.RobotsTxtHandler
{
    public class RobotsContentService : IRobotsContentService
    {
        private static ILog logger = LogManager.GetLogger(typeof(RobotsContentService));

        private string CurrentSiteKey
        {
            get
            {
                // Look for a specific host name mapping for the current host name in the config
                var hostLookup = from HostNameCollection hosts in EPiServerFrameworkSection.Instance.SiteHostMapping
                                 from HostNameElement host in hosts
                                 where host.Name == HttpContext.Current.Request.Url.Host
                                 select host;
                var hostLookUpArray = hostLookup.ToArray();

                if (hostLookUpArray.Any())
                {
                    // If the host is explicitly listed then return the key for the siteId and host
                    return GetSiteKey(EPiServer.Configuration.Settings.Instance.Parent.SiteId, hostLookUpArray[0].Name);
                }
                
                // Otherwise this is the default "*" mapping of the site
                return GetSiteKey(EPiServer.Configuration.Settings.Instance.Parent.SiteId, "*");
            }
        }

        public string GetRobotsContent()
        {
            return GetRobotsContent(this.CurrentSiteKey);
        }

        public string GetRobotsContent(string robotsKey)
        {
            try
            {
                // Always try to retrieve from cache first
                var cache = CacheManager.Get(GetCacheKey(robotsKey));
                if (cache != null)
                    return (cache as RobotsTxtData).RobotsTxtContent;

                using (var robotsDataStore = typeof(RobotsTxtData).GetStore())
                {
                    // Look up the content in the DDS
                    var result = robotsDataStore.Find<RobotsTxtData>("Key", robotsKey).FirstOrDefault();
                    if (result == null)
                    {
                        // OK there is nothing found so create some default content
                        result = new RobotsTxtData
                        {
                            Key = robotsKey,
                            RobotsTxtContent = GetDefaultRobotsContent()
                        };

                        SaveRobotsContent(result.RobotsTxtContent, robotsKey);
                    }

                    // Ensure we cache the result using the EPiServer cache manager to ensure 
                    // everything works in a load balanced/mirrored environement
                    CacheManager.Add(GetCacheKey(robotsKey), result);
                    return result.RobotsTxtContent;
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error getting robots.txt content, returning default robots.txt content", ex);
                return GetDefaultRobotsContent();
            }
        }

        public void SaveRobotsContent(string robotsContent)
        {
            SaveRobotsContent(robotsContent, CurrentSiteKey);
        }

        public void SaveRobotsContent(string robotsContent, string robotsKey)
        {
            try
            {
                // Save the updated robots content down into the DDS
                using (var robotsDataStore = typeof(RobotsTxtData).GetStore())
                {
                    var result = robotsDataStore.Find<RobotsTxtData>("Key", robotsKey).FirstOrDefault();
                    if (result == null)
                    {
                        robotsDataStore.Save(new RobotsTxtData { Key = robotsKey, RobotsTxtContent = robotsContent });
                    }
                    else
                    {
                        result.RobotsTxtContent = robotsContent;
                        robotsDataStore.Save(result);

                        // Ensure that the cache item is removed if it already exists
                        if (CacheManager.Get(this.GetCacheKey(robotsKey)) != null)
                            CacheManager.Remove(this.GetCacheKey(robotsKey));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error saving robots.txt content", ex);
            }
        }

        public string GetSiteKey(string siteId, string siteHost)
        {
            return siteId + " > " + siteHost;
        }

        private string GetCacheKey(string robotsKey)
        {
            return "EPiRobotsCache_{0}" + robotsKey;
        }

        private string GetDefaultRobotsContent()
        {
            var defaultText = new StringBuilder();
            defaultText.Append("User-agent: *" + System.Environment.NewLine);

            try
            {
                // By default we will remove the UI and Util URL paths
                defaultText.Append("Disallow: " + EPiServer.Configuration.Settings.Instance.UIUrl.OriginalString.TrimStart("~".ToCharArray()) + System.Environment.NewLine);
                defaultText.Append("Disallow: " + EPiServer.Configuration.Settings.Instance.UtilUrl.OriginalString.TrimStart("~".ToCharArray()) + System.Environment.NewLine);
            }
            catch (Exception ex)
            {
                logger.Error("Error accessing EPiServer configuration settings", ex);
            }

            return defaultText.ToString();
        }
    }
}