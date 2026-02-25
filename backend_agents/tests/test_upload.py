import requests

def test_upload():
    # 测试健康检查
    health_response = requests.get("http://localhost:8000/v1/health")
    print(f"Health check: {health_response.status_code} - {health_response.json()}")
    
    # 测试文件上传
    with open("tests/test.txt", "rb") as f:
        files = {"file": ("test.txt", f, "text/plain")}
        upload_response = requests.post("http://localhost:8000/v1/files/upload", files=files)
        print(f"Upload test: {upload_response.status_code}")
        print(f"Response: {upload_response.json()}")
    
    # 测试文件列表
    list_response = requests.get("http://localhost:8000/v1/files/")
    print(f"File list: {list_response.status_code}")
    print(f"Files: {list_response.json()}")

if __name__ == "__main__":
    test_upload()
