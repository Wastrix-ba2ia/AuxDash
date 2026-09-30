using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Web.Script.Serialization;

namespace SideScreenMonitor {
    public static class ChineseDate {
        public static string Lunar(DateTime date) {
            try {
                var calendar = new ChineseLunisolarCalendar();
                int year = calendar.GetYear(date), month = calendar.GetMonth(date), day = calendar.GetDayOfMonth(date);
                int leap = calendar.GetLeapMonth(year); bool isLeap = month == leap;
                if (leap > 0 && month >= leap) month--;
                string[] months = { "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };
                string[] digits = { "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
                string dayName = day <= 10 ? "初" + digits[day - 1] : day < 20 ? "十" + digits[day - 11] : day == 20 ? "二十" : day < 30 ? "廿" + digits[day - 21] : "三十";
                return "农历" + (isLeap ? "闰" : "") + months[month - 1] + "月" + dayName;
            } catch (ArgumentOutOfRangeException) { return "农历日期超出范围"; }
        }
        public static string Weekday(DateTime date) { return "星期" + "日一二三四五六"[(int)date.DayOfWeek]; }
    }
    public sealed class WeatherReading {
        public double Temperature, TodayLow, TodayHigh, TomorrowLow, TomorrowHigh;
        public int Code, TomorrowCode;
        public DateTime Observed, Day, Received;
        public bool Cached; public string Location="长春", TimeZone="Asia/Shanghai";
        public bool IsCurrent {
            get {DateTime local;try{local=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(TimeZone));}catch{local=DateTime.UtcNow.AddHours(8);}return Day.Date==local.Date&&(local-Observed).TotalHours<=2&&Observed<=local.AddMinutes(30);}
        }
        public string Summary() {
            if(!IsCurrent)return Location+"  天气数据已过期，等待更新";
            return Location+"  " + WeatherClient.Condition(Code) + " " + Temperature.ToString("0.0", CultureInfo.InvariantCulture) + "°C  |  今天 " + TodayLow.ToString("0") + "～" + TodayHigh.ToString("0") + "°C  |  明天 " + WeatherClient.Condition(TomorrowCode) + " " + TomorrowLow.ToString("0") + "～" + TomorrowHigh.ToString("0") + "°C" + (Cached ? "  [缓存]" : "");
        }
    }
    public static class WeatherClient {
        public sealed class Location { public string Name,Admin1,Country,TimeZone; public double Latitude,Longitude; public override string ToString(){return Name+(string.IsNullOrEmpty(Admin1)?"":" · "+Admin1)+(string.IsNullOrEmpty(Country)?"":" · "+Country);} }
        static string CachePath(double latitude,double longitude) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"weather-cache-"+latitude.ToString("0.####",CultureInfo.InvariantCulture)+"-"+longitude.ToString("0.####",CultureInfo.InvariantCulture)+".json"); }
        public static List<Location> Search(string name) {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            string url="https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(name)+"&count=12&language=zh&format=json";
            var req=(HttpWebRequest)WebRequest.Create(url);req.Timeout=10000;req.UserAgent="NeonSideScreenMonitor/1.0";
            using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){
                var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());var list=new List<Location>();object raw;
                if(!root.TryGetValue("results",out raw)||!(raw is IList))return list;
                foreach(var item in (IList)raw){var row=item as Dictionary<string,object>;if(row==null)continue;list.Add(new Location{Name=Convert.ToString(row["name"]),Admin1=row.ContainsKey("admin1")?Convert.ToString(row["admin1"]):"",Country=row.ContainsKey("country")?Convert.ToString(row["country"]):"",TimeZone=row.ContainsKey("timezone")?Convert.ToString(row["timezone"]):"Asia/Shanghai",Latitude=N(row["latitude"]),Longitude=N(row["longitude"]) });}
                return list;
            }
        }
        static double N(object value) { if (value == null) throw new InvalidDataException("Missing weather value"); return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        public static WeatherReading Parse(string json) {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var current = (Dictionary<string, object>)root["current"];
            var daily = (Dictionary<string, object>)root["daily"];
            var days = (IList)daily["time"]; var codes = (IList)daily["weather_code"]; var lows = (IList)daily["temperature_2m_min"]; var highs = (IList)daily["temperature_2m_max"];
            DateTime today = DateTime.Parse(Convert.ToString(days[0]), CultureInfo.InvariantCulture);
            if (DateTime.Parse(Convert.ToString(days[1]), CultureInfo.InvariantCulture).Date != today.AddDays(1).Date) throw new InvalidDataException("Invalid forecast dates");
            return new WeatherReading { Temperature = N(current["temperature_2m"]), Code = (int)N(current["weather_code"]), Observed = DateTime.Parse(Convert.ToString(current["time"]), CultureInfo.InvariantCulture), Day = today, TodayLow = N(lows[0]), TodayHigh = N(highs[0]), TomorrowLow = N(lows[1]), TomorrowHigh = N(highs[1]), TomorrowCode = (int)N(codes[1]), Received = DateTime.UtcNow };
        }
        public static WeatherReading LoadCache(double latitude,double longitude,string location,string timezone) { try { var r = Parse(File.ReadAllText(CachePath(latitude,longitude))); r.Cached=true;r.Location=location;r.TimeZone=timezone;return r; } catch{return null;} }
        public static WeatherReading Fetch(double latitude,double longitude,string location,string timezone) {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url="https://api.open-meteo.com/v1/forecast?latitude="+latitude.ToString(CultureInfo.InvariantCulture)+"&longitude="+longitude.ToString(CultureInfo.InvariantCulture)+"&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone="+Uri.EscapeDataString(timezone)+"&forecast_days=3";
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 10000; request.ReadWriteTimeout = 10000; request.UserAgent = "NeonSideScreenMonitor/1.0";
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) {
                string json = reader.ReadToEnd(); var r = Parse(json);r.Location=location;r.TimeZone=timezone;
                string cache=CachePath(latitude,longitude);
                try { File.WriteAllText(cache+".tmp",json);if(File.Exists(cache))File.Replace(cache+".tmp",cache,null);else File.Move(cache+".tmp",cache); } catch { }
                return r;
            }
        }
        public static string Condition(int code) {
            switch (code) {
                case 0: return "晴"; case 1: return "晴间多云"; case 2: return "多云"; case 3: return "阴";
                case 45: case 48: return "雾";
                case 51: case 53: case 55: return "毛毛雨";
                case 56: case 57: case 66: case 67: return "冻雨";
                case 61: return "小雨"; case 63: return "中雨"; case 65: return "大雨";
                case 71: return "小雪"; case 73: return "中雪"; case 75: return "大雪"; case 77: return "米雪";
                case 80: case 81: case 82: return "阵雨"; case 85: case 86: return "阵雪";
                case 95: return "雷雨"; case 96: case 99: return "雷雨冰雹"; default: return "天气未知";
            }
        }
    }
}
