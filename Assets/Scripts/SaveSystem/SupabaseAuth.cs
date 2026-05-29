using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DLS.SaveSystem
{
	public static class SupabaseAuth
	{
			public static readonly string SupabaseUrl = Env.Get("SUPABASE_URL") ?? "https://bodbbodwbfsxjzxfnxon.supabase.co";
			public static readonly string AnonKey = Env.Get("SUPABASE_ANON_KEY") ?? "sb_publishable_wCxYTqeD3Z2Hob_NlELBcw_Gxtr9sGv";

		const string PrefAccessToken = "DLS_AccessToken";
		const string PrefUserId = "DLS_UserId";
		const string PrefUserEmail = "DLS_UserEmail";

		public static string AccessToken { get; private set; }
		public static string UserId { get; private set; }
		public static string UserEmail { get; private set; }
		public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

		static SupabaseAuth()
		{
			AccessToken = PlayerPrefs.GetString(PrefAccessToken, null);
			UserId = PlayerPrefs.GetString(PrefUserId, null);
			UserEmail = PlayerPrefs.GetString(PrefUserEmail, null);
		}

		public static async Task<(bool success, string error)> SignUp(string email, string password)
		{
			string body = $"{{\"email\":\"{Escape(email)}\",\"password\":\"{Escape(password)}\"}}";
			return await SendAuthRequest(SupabaseUrl + "/auth/v1/signup", body);
		}

		public static async Task<(bool success, string error)> SignIn(string email, string password)
		{
			string body = $"{{\"email\":\"{Escape(email)}\",\"password\":\"{Escape(password)}\"}}";
			return await SendAuthRequest(SupabaseUrl + "/auth/v1/token?grant_type=password", body);
		}

		public static void SignOut()
		{
			AccessToken = null;
			UserId = null;
			UserEmail = null;
			PlayerPrefs.DeleteKey(PrefAccessToken);
			PlayerPrefs.DeleteKey(PrefUserId);
			PlayerPrefs.DeleteKey(PrefUserEmail);
			PlayerPrefs.Save();
		}

		public const string ConfirmEmailSentinel = "CONFIRM_EMAIL";

		static async Task<(bool success, string error)> SendAuthRequest(string url, string body)
		{
			bool isSignUp = url.Contains("/signup");
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			using UnityWebRequest req = new(url, "POST");
			req.uploadHandler = new UploadHandlerRaw(bytes);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("apikey", AnonKey);
			req.SetRequestHeader("Content-Type", "application/json");

			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();

			string responseText = req.downloadHandler.text;

			if (req.result != UnityWebRequest.Result.Success)
				return (false, ExtractJsonMessage(responseText) ?? req.error);

			AuthResponse response = JsonUtility.FromJson<AuthResponse>(responseText);
			if (response?.access_token == null)
				return (false, isSignUp ? ConfirmEmailSentinel : "Sign in failed. Check your credentials.");

			AccessToken = response.access_token;
			UserId = response.user?.id;
			UserEmail = response.user?.email;

			PlayerPrefs.SetString(PrefAccessToken, AccessToken);
			PlayerPrefs.SetString(PrefUserId, UserId ?? "");
			PlayerPrefs.SetString(PrefUserEmail, UserEmail ?? "");
			PlayerPrefs.Save();

			return (true, null);
		}

		static string ExtractJsonMessage(string json)
		{
			foreach (string key in new[] { "\"msg\":\"", "\"message\":\"", "\"error_description\":\"" })
			{
				int s = json.IndexOf(key, StringComparison.Ordinal);
				if (s < 0) continue;
				s += key.Length;
				int e = json.IndexOf('"', s);
				if (e > s) return json.Substring(s, e - s);
			}
			return null;
		}

		static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

		[Serializable] class AuthResponse { public string access_token; public string refresh_token; public AuthUser user; }
		[Serializable] class AuthUser { public string id; public string email; }
	}
}
