import sys
import json

def main():
    if len(sys.argv) < 2:
        print(json.dumps({"error": "URL argument required"}))
        sys.exit(1)

    url = sys.argv[1]

    try:
        from torah_dl import extract
        result = extract(url)
        print(json.dumps({
            "download_url": result.download_url,
            "title": result.title,
            "file_format": result.file_format,
            "file_name": result.file_name
        }))
    except Exception as e:
        print(json.dumps({"error": str(e)}))
        sys.exit(1)

if __name__ == "__main__":
    main()
