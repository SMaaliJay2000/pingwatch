namespace PingWatch.Domain;

public enum PlanTier { Free, Starter, Pro }

public enum SubscriptionStatus { None, Active, PastDue, Cancelled, Expired }

public enum UserRole { Owner }

public enum MonitorType { Http, Keyword, Tcp, Ssl, Heartbeat }

public enum MonitorStatus { Pending, Up, Down, Paused }

public enum CheckMethod { Get, Head, Post }

public enum KeywordMode { Contains, NotContains }

public enum AlertChannelType { Email, Webhook, Slack, Discord, Telegram, Sms }

public enum AlertType { Down, Up, SslWarning }
