using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VersionrWeb.Models
{
	// http://stackoverflow.com/questions/11/how-can-relative-time-be-calculated-in-c/628203#628203
	public static class DateTimeExtensions
	{
		/// <summary>
		/// Compares a supplied date to the current date and generates a friendly English 
		/// comparison ("5 days ago", "5 days from now")
		/// </summary>
		/// <param name="date">The date to convert</param>
		/// <param name="approximate">When off, calculate timespan down to the second.
		/// When on, approximate to the largest round unit of time.</param>
		/// <returns></returns>
		public static string ToRelativeDateString(this DateTime value, bool approximate = true)
		{
			StringBuilder sb = new StringBuilder();

			string suffix = (value > DateTime.UtcNow) ? " from now" : " ago";

			TimeSpan timeSpan = new TimeSpan(Math.Abs(DateTime.UtcNow.Subtract(value).Ticks));

			if (timeSpan.Days >= 365 && approximate)
			{
				int years = timeSpan.Days / 365;
				sb.AppendFormat("{0} {1}", years,
				  (years > 1) ? "years" : "year");
				return sb.ToString() + suffix;
			}
			else if (timeSpan.Days >= 30 && approximate)
			{
				int months = timeSpan.Days / 30;
				sb.AppendFormat("{0} {1}", months,
				  (months > 1) ? "months" : "month");
				return sb.ToString() + suffix;
			}
			else if (timeSpan.Days >= 7)
			{
				int weeks = timeSpan.Days / 7;
				timeSpan.Subtract(TimeSpan.FromDays(weeks * 7));

				sb.AppendFormat("{0} {1}", weeks,
				  (weeks > 1) ? "weeks" : "week");
				if (approximate) return sb.ToString() + suffix;
			}

			if (timeSpan.Days > 0)
			{
				sb.AppendFormat("{0}{1} {2}", (sb.Length > 0) ? ", " : string.Empty,
					timeSpan.Days, (timeSpan.Days > 1) ? "days" : "day");
				if (approximate) return sb.ToString() + suffix;
			}
			if (timeSpan.Hours > 0)
			{
				sb.AppendFormat("{0}{1} {2}", (sb.Length > 0) ? ", " : string.Empty,
				  timeSpan.Hours, (timeSpan.Hours > 1) ? "hours" : "hour");
				if (approximate) return sb.ToString() + suffix;
			}
			if (timeSpan.Minutes > 0)
			{
				sb.AppendFormat("{0}{1} {2}", (sb.Length > 0) ? ", " : string.Empty,
				  timeSpan.Minutes, (timeSpan.Minutes > 1) ? "minutes" : "minute");
				if (approximate) return sb.ToString() + suffix;
			}
			if (timeSpan.Seconds > 0)
			{
				sb.AppendFormat("{0}{1} {2}", (sb.Length > 0) ? ", " : string.Empty,
				  timeSpan.Seconds, (timeSpan.Seconds > 1) ? "seconds" : "second");
				if (approximate) return sb.ToString() + suffix;
			}
			if (sb.Length == 0) return "right now";

			sb.Append(suffix);
			return sb.ToString();
		}
	}
}
