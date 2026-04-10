"""Demo of streaming list."""

from __future__ import annotations

import argparse
import json
import sys
from urllib import error as urllib_error
from urllib import parse as urllib_parse
from urllib import request as urllib_request

import grpc


def parse_args() -> argparse.Namespace:
  parser = argparse.ArgumentParser(
      description="YouTube Live Chat StreamList gRPC demo"
  )
  parser.add_argument(
      "token",
      help="API key or OAuth access token",
  )
  parser.add_argument(
      "live_chat_id",
      nargs="?",
      help="YouTube liveChatId",
  )
  parser.add_argument(
      "--video-id",
      help="YouTube live video ID used to resolve liveChatId",
  )
  parser.add_argument(
      "--auth-mode",
      choices=("api-key", "oauth"),
      default="api-key",
      help="Authentication mode to use",
  )
  return parser.parse_args()


def build_metadata(args: argparse.Namespace) -> tuple[tuple[str, str], ...]:
  if args.auth_mode == "oauth":
    return (("authorization", f"Bearer {args.token}"),)

  return (("x-goog-api-key", args.token),)


def fetch_json(
    url: str,
    headers: dict[str, str] | None = None,
) -> dict[str, object]:
  request = urllib_request.Request(url, headers=headers or {})

  try:
    with urllib_request.urlopen(request) as response:
      return json.load(response)
  except urllib_error.HTTPError as error:
    body = error.read().decode("utf-8", errors="replace")
    raise RuntimeError(
        f"HTTP {error.code} while calling {url}: {body}"
    ) from error


def resolve_live_chat_id_from_video(
    token: str,
    video_id: str,
    auth_mode: str,
) -> str:
  query = {
      "part": "liveStreamingDetails",
      "id": video_id,
  }
  headers: dict[str, str] = {}

  if auth_mode == "api-key":
    query["key"] = token
  else:
    headers["Authorization"] = f"Bearer {token}"

  url = (
      "https://www.googleapis.com/youtube/v3/videos?"
      f"{urllib_parse.urlencode(query)}"
  )
  payload = fetch_json(url, headers=headers)
  items = payload.get("items")
  if not isinstance(items, list) or not items:
    raise ValueError(f"Video not found: {video_id}")

  live_streaming_details = items[0].get("liveStreamingDetails")
  if not isinstance(live_streaming_details, dict):
    raise ValueError(
        "liveStreamingDetails is missing. Confirm that the video is a live stream."
    )

  live_chat_id = live_streaming_details.get("activeLiveChatId")
  if not isinstance(live_chat_id, str) or not live_chat_id:
    raise ValueError(
        "activeLiveChatId is missing. Confirm that the stream is currently live."
    )

  return live_chat_id


def resolve_live_chat_id_from_broadcasts(token: str) -> str:
  query = {
      "part": "snippet,status",
      "mine": "true",
      "maxResults": "10",
  }
  url = (
      "https://www.googleapis.com/youtube/v3/liveBroadcasts?"
      f"{urllib_parse.urlencode(query)}"
  )
  payload = fetch_json(
      url,
      headers={"Authorization": f"Bearer {token}"},
  )
  items = payload.get("items")
  if not isinstance(items, list):
    raise ValueError("No broadcasts were returned for the authorized account.")

  for item in items:
    if not isinstance(item, dict):
      continue

    status = item.get("status")
    snippet = item.get("snippet")
    if not isinstance(status, dict) or not isinstance(snippet, dict):
      continue

    life_cycle_status = status.get("lifeCycleStatus")
    if life_cycle_status not in ("live", "liveStarting"):
      continue

    live_chat_id = snippet.get("liveChatId")
    if isinstance(live_chat_id, str) and live_chat_id:
      return live_chat_id

  raise ValueError(
      "No active broadcast with liveChatId was found for the authorized account."
  )


def resolve_live_chat_id(args: argparse.Namespace) -> str:
  if args.live_chat_id:
    return args.live_chat_id

  if args.video_id:
    return resolve_live_chat_id_from_video(args.token, args.video_id, args.auth_mode)

  if args.auth_mode == "oauth":
    return resolve_live_chat_id_from_broadcasts(args.token)

  raise ValueError("Provide live_chat_id or --video-id when using API key mode.")


def main() -> int:
  args = parse_args()
  metadata = build_metadata(args)

  try:
    live_chat_id = resolve_live_chat_id(args)
  except (RuntimeError, ValueError) as error:
    print(error, file=sys.stderr)
    return 1

  print(f"Resolved liveChatId: {live_chat_id}")

  try:
    import stream_list_pb2
    import stream_list_pb2_grpc
  except ImportError as error:
    print(
        "Missing generated gRPC Python modules. Generate stream_list_pb2.py and "
        "stream_list_pb2_grpc.py from stream_list.proto before running this demo.",
        file=sys.stderr,
    )
    print(error, file=sys.stderr)
    return 1

  creds = grpc.ssl_channel_credentials()
  try:
    with grpc.secure_channel(
        "dns:///youtube.googleapis.com:443", creds
    ) as channel:
      stub = stream_list_pb2_grpc.V3DataLiveChatMessageServiceStub(channel)
      next_page_token = None

      while True:
        request = stream_list_pb2.LiveChatMessageListRequest(
            part=["snippet", "authorDetails"],
            live_chat_id=live_chat_id,
            max_results=20,
            page_token=next_page_token,
        )

        has_next_page = False
        for response in stub.StreamList(request, metadata=metadata):
          print(response)
          next_page_token = response.next_page_token
          has_next_page = bool(next_page_token)

          if not has_next_page:
            break

        if not has_next_page:
          break
  except grpc.RpcError as error:
    print(f"gRPC error code: {error.code()}", file=sys.stderr)
    print(f"gRPC error details: {error.details()}", file=sys.stderr)
    return 1

  return 0


if __name__ == "__main__":
  raise SystemExit(main())
