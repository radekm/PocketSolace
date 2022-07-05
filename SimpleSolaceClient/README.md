# Scenarios to try

- Not connected to Solace because of:
  - Host does not exist.
  - User does not exist.
- Successfully connected to Solace.
- Subscribe to:
  - Topic which is not subscribed.
  - Topic which is already subscribed.
  - Invalid topic pattern.
- Received message from Solace.
- Unsubscribe from:
  - Topic which is already subscribed.
  - Topic which is not subscribed.
  - Invalid topic pattern.
- Normally disconnected from Solace.
- Disconnected from Solace because of:
  - Network connection down (eg. Wi-Fi turned off).
  - VPN down.
  - Message channel gets full.
