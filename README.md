# UIT-GO
| Name | Student ID | 
| ------ | ------ |
| [To Phu Quy](https://github.com/seven-up-seven) | 23521320 |
| [Nguyen Minh Quang](https://github.com/Whiteknight12) | 23521286 |
| [Nguyen Hoang Hien](https://github.com/nghoanghien) |23520461 |

Hệ thống backend mô phỏng app đặt xe (kiểu gọi tài xế gần nhất), tách thành nhiều service nhỏ và giao tiếp qua 1 API Gateway.

## Kiến trúc nhanh gọn

* `api-gateway`

  * .NET + YARP Reverse Proxy
  * Public entrypoint cho FE / client
  * Port host: **8080**
  * Forward request đến từng service nội bộ theo prefix:

    * `/api/users/**`  → `user-service`
    * `/api/trips/**`  → `trip-service`
    * `/api/drivers/**` → `driver-service`
      (được config trong `appsettings.json` của ApiGateway) 

* `user-service`

  * Quản lý user / auth
  * DB riêng: Postgres `uitgo_user`
  * Có JWT (env `Jwt__Secret` đã set sẵn trong `docker-compose.yml`) 

* `trip-service`

  * Tạo chuyến đi, lấy chuyến đi, cancel chuyến đi
  * DB riêng: Postgres `uitgo_trip`
  * Redis để lock trip, tìm tài xế
  * Gọi gRPC sang `driver-service` để hỏi tài xế gần nhất / assign tài xế (sockets HTTP/2 unencrypted đã bật ở `Program.cs`) 

* `driver-service`

  * Quản lý tài xế
  * Cập nhật vị trí, đánh dấu online, hoàn tất chuyến
  * DB riêng: Postgres `uitgo_driver`
  * Redis GEO (`drivers:online`) để lưu vị trí tài xế real-time
  * Expose gRPC `DriverQuery` cho TripService dùng (GetDriverInfo, MarkTripAssigned) 

* Infra chia riêng trong docker-compose:

  * Postgres x3 (user / trip / driver)
  * Redis 1 instance (chia sẻ realtime, match tài xế)
    → service `uit-go-redis` trong `docker-compose.yml` port `6379` 

---

## 1. Yêu cầu trước khi chạy

### Bắt buộc cài:

* **Docker Desktop** (Windows/Mac) hoặc Docker Engine (Linux)
* **Docker Compose** (Compose V2, thường đã tích hợp sẵn vào Docker Desktop)

Kiểm tra nhanh:

```bash
docker --version
docker compose version
```

Nếu 2 lệnh trên đều chạy ok → tiếp tục.

> .NET SDK **không bắt buộc** để chạy bằng Docker vì mỗi service đã có Dockerfile riêng (`deploy/docker/*.Dockerfile`) build/publish .NET 8 vào runtime image. 

---

## 2. Cấu trúc liên quan đến deploy

```text
docker-compose.yml
deploy/
  docker/
    ApiGateway.Dockerfile
    DriverService.Dockerfile
    TripService.Dockerfile
    UserService.Dockerfile
ApiGateway/
DriverService/
TripService/
UserService/
Shared/
```

Điểm đáng chú ý:

* Mỗi Dockerfile:

  * build bằng `mcr.microsoft.com/dotnet/sdk:8.0`
  * publish ra `/app/out`
  * runtime là `mcr.microsoft.com/dotnet/aspnet:8.0`
  * expose cổng nội bộ `8080`
  * set `ASPNETCORE_URLS=http://+:8080` để service listen HTTP thường, không HTTPS trong container. 

* `docker-compose.yml` map cổng như sau:

  * `api-gateway` → host `8080:8080`
  * `user-service` → host `5001:8080`
  * `trip-service` → host `5002:8080`
  * `driver-service` → host `5003:8080`
  * Redis → host `6379:6379`
  * Postgres:

    * user-db → host `5433:5432`
    * trip-db → host `5434:5432`
    * driver-db → host `5435:5432` 

Env connection string trong compose đã trỏ đúng service name nội bộ (VD: `Host=postgres-user;Port=5432;...`) nên container có thể nói chuyện với nhau qua network docker. 
Ngoài ra mỗi API service tự động chạy EF Core migration on startup (ví dụ DriverService làm `db.Database.Migrate();` trong `Program.cs`). 

---

## 3. Chạy hệ thống

Clone repo rồi chạy:

```bash
docker compose up --build
```

Giải thích:

* `--build`: ép build lại image từ code hiện tại
* Compose sẽ:

  * start Postgres, Redis
  * build và start `user-service`, `trip-service`, `driver-service`
  * cuối cùng start `api-gateway`

Nếu chạy ok, bạn sẽ thấy log tương kiểu:

* `api-gateway` listening on `http://+:8080`
* Mỗi service .NET log ra Swagger ở chế độ Development
* Redis ready to accept connections

Ở chế độ mặc định:

* Gọi API nên gọi qua **Gateway**: `http://localhost:8080/...`
* (Bạn *có thể* gọi thẳng từng service qua 5001 / 5002 / 5003 để debug, nhưng luồng chính dùng gateway)

---

## 4. Test nhanh bằng curl

> Tất cả ví dụ dưới đây đều gọi vào `api-gateway` qua `http://localhost:8080`.

### 4.1. User Service (Auth / Users)

UserService hiện expose controller AuthController (đăng ký / đăng nhập) ở `UserService.Api`. Tên route cụ thể trong code không nằm trong snippet ở trên, nhưng theo convention trong repo thì gateway route `/api/users/**` sẽ forward sang `user-service`. 

Ví dụ (giả định) đăng ký user mới:

```bash
curl -X POST http://localhost:8080/api/users/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "demo@example.com",
    "password": "string123",
    "fullName": "Demo User"
  }'
```

Ví dụ (giả định) login:

```bash
curl -X POST http://localhost:8080/api/users/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "demo@example.com",
    "password": "string123"
  }'
```

Nếu login thành công, service sẽ dùng `Jwt__Secret` (được inject từ env trong docker-compose) để tạo JWT. JWT này dùng cho future gọi các API cần auth (phase sau). 

> Lưu ý: route cụ thể `/auth/login` / `/auth/register` có thể hơi khác tùy code trong `AuthController.cs`. Nếu 404, hãy mở Swagger trực tiếp qua cổng service 5001 để xem đúng route (xem mục **5. Swagger**).

---

### 4.2. Trip Service (Tạo chuyến đi)

`TripsController`:

* `POST /api/trips`

  * tạo chuyến đi mới
  * TripService sẽ:

    * generate `Trip.Id`
    * gán `PassengerId` tạm thời (hardcode random GUID trong phase hiện tại)
    * gọi logic `_tripService.CreateAsync(...)` để lưu DB, chọn tài xế, vv. 

Request body mẫu (`CreateTripRequest` theo code):

```json
{
  "pickupLat": 10.762622,
  "pickupLng": 106.660172,
  "dropoffLat": 10.780000,
  "dropoffLng": 106.700000
}
```

Test bằng `curl`:

```bash
curl -X POST http://localhost:8080/api/trips \
  -H "Content-Type: application/json" \
  -d '{
    "pickupLat": 10.762622,
    "pickupLng": 106.660172,
    "dropoffLat": 10.780000,
    "dropoffLng": 106.700000
  }'
```

Kết quả trả về sẽ là object Trip vừa tạo (bao gồm `id`, `passengerId`, toạ độ start/end, status ban đầu). 

* `GET /api/trips/{id}`

  * lấy thông tin chuyến đi theo `id`

```bash
curl http://localhost:8080/api/trips/REPLACE_WITH_TRIP_ID
```

* `POST /api/trips/{id}/cancel`

  * cancel chuyến

```bash
curl -X POST http://localhost:8080/api/trips/REPLACE_WITH_TRIP_ID/cancel
```

* `GET /api/trips/health`

  * healthcheck service TripService

```bash
curl http://localhost:8080/api/trips/health
# expect "trip ok"
```

Tại sao TripService biết tài xế nào gần?
→ TripService dùng `TripMatchService`, truy vấn Redis GEO key `drivers:online` để tìm tài xế trong bán kính, sau đó confirm lại qua gRPC `DriverQuery` để chắc tài xế còn available. Nếu chọn được tài xế, nó sẽ `MarkTripAssigned` để lock tài xế đó. 

---

### 4.3. Driver Service (Tài xế / Vị trí)

`DriversController` có các endpoint: 

#### Cập nhật vị trí tài xế

`POST /api/drivers/{driverId}/location`

Body:

```json
{
  "lat": 10.762622,
  "lng": 106.660172
}
```

Test:

```bash
curl -X POST http://localhost:8080/api/drivers/00000000-0000-0000-0000-000000000001/location \
  -H "Content-Type: application/json" \
  -d '{
    "lat": 10.762622,
    "lng": 106.660172
  }'
```

Luồng xử lý:

* Lưu vị trí driver vào Postgres (bằng EFCore repo `EfDriverRepository`)
* Đồng bộ vị trí vào Redis GEO `"drivers:online"` (key `driver:{driverId}` cũng được update) thông qua `DriverLocationService`. 

#### Đánh dấu online/offline

`POST /api/drivers/{driverId}/online`

Body:

```json
{
  "online": true
}
```

Test:

```bash
curl -X POST http://localhost:8080/api/drivers/00000000-0000-0000-0000-000000000001/online \
  -H "Content-Type: application/json" \
  -d '{"online": true}'
```

Service sẽ:

* upsert tài xế vào DB (nếu chưa tồn tại sẽ tạo record mới với Id đó)
* set `Online` = true/false
* update Redis để reflect trạng thái có mặt trong vùng tìm kiếm. 

#### Kết thúc chuyến

`POST /api/drivers/{driverId}/trip-finished`

Test:

```bash
curl -X POST http://localhost:8080/api/drivers/00000000-0000-0000-0000-000000000001/trip-finished
```

Luồng:

* Gỡ trạng thái bận khỏi Redis:

  * `available` = "1"
  * `current_trip_id` = ""
* (Gợi ý future: log lịch sử chuyến trong DB) 

#### Healthcheck

`GET /api/drivers/health`

```bash
curl http://localhost:8080/api/drivers/health
# expect "driver ok"
```

---

## 5. Swagger / Postman

### Swagger

Trong môi trường Development, mỗi service .NET enable Swagger UI khi chạy trực tiếp (không qua gateway).
Bạn có thể mở:

* UserService: [http://localhost:5001/swagger](http://localhost:5001/swagger)
* TripService: [http://localhost:5002/swagger](http://localhost:5002/swagger)
* DriverService: [http://localhost:5003/swagger](http://localhost:5003/swagger)
* ApiGateway: [http://localhost:8080/swagger](http://localhost:8080/swagger) (gateway cũng AddSwaggerGen, nhưng bản chất gateway chủ yếu reverse proxy nên Swagger ở đây chỉ show endpoint nội bộ của gateway, không phải merge spec của các service) 

Swagger giúp bạn coi đúng route thực tế, body schema thực tế để import vào Postman nếu curl mẫu ở trên khác với code hiện thời.

### Postman

Cách đơn giản:

1. Mở Postman
2. Tạo Collection `uit-go`
3. Thêm request:

   * `POST http://localhost:8080/api/trips`
   * Body raw JSON (application/json)
4. Send và xem response.

Bạn cũng có thể import Swagger của từng service trực tiếp bằng URL `http://localhost:5002/swagger/v1/swagger.json` (v.v...) để Postman generate sẵn request.

> Nếu Postman cảnh báo self-signed SSL thì cứ gọi bản `http://localhost:500x` (HTTP) thay vì `https://localhost:7xxx` vì trong container hiện tại chúng ta chạy HTTP thuần (`ASPNETCORE_URLS=http://+:8080`, `UseHttpsRedirection()` thậm chí đã bị comment ở ApiGateway). 

---

## 6. Debug nhanh

### Kiểm tra container đang chạy

```bash
docker ps
```

### Xem log của 1 service

```bash
docker compose logs -f trip-service
```

### Kết nối DB để xem bảng

Ví dụ Trip DB (Postgres trip-service):

```bash
psql -h localhost -p 5434 -U postgres -d uitgo_trip
# password: postgres
```

Bên trong psql:

```sql
\d "Trips";
SELECT * FROM "Trips";
```

> EF Core migration tự chạy mỗi lần service start (ví dụ DriverService gọi `db.Database.Migrate()` trong Program). Nếu bạn xóa volume docker rồi up lại, bảng sẽ được tạo lại sạch. 

---

## 7. Tóm tắt luồng gọi cơ bản

1. Tài xế A bật online và update vị trí
   → `POST /api/drivers/{driverId}/location` & `/online`
   → DriverService lưu vào DB + Redis GEO.

2. Hành khách gửi yêu cầu chuyến
   → `POST /api/trips`
   → TripService:

   * tạo record Trip trong Postgres riêng
   * hỏi Redis để tìm tài xế gần nhất
   * confirm tài xế còn rảnh qua gRPC `DriverQuery` (DriverService gRPC server)
   * đánh dấu tài xế đó là bận (`MarkTripAssigned`) qua gRPC
   * trả về Trip đã gán tài xế.

3. FE/future client có thể poll `GET /api/trips/{id}` để xem trạng thái.

4. Khi tài xế kết thúc chuyến
   → `POST /api/drivers/{driverId}/trip-finished`
   → DriverService set lại available=1 trong Redis. 

---

## 8. Ghi chú thêm

* Hiện tại `api-gateway` dùng YARP ReverseProxy config trong `appsettings.json`, không phải Nginx. Cấu hình Nginx tồn tại dưới `nginx_gateway/` để tham khảo tương lai, nhưng trong `docker-compose.yml` nó đang bị comment (service `gateway`). 
* Auth/JWT đang còn đơn giản (env `Jwt__Secret` đặt thẳng trong compose). Tuyệt đối KHÔNG commit secret thật trong production.
* Chưa bật HTTPS trong container cho đỡ rườm, nên mọi request demo đều là `http://`.

---

## 9. TL;DR chạy nhanh

```bash
# 1. bật toàn bộ stack
docker compose up --build

# 2. tạo trip mới
curl -X POST http://localhost:8080/api/trips \
  -H "Content-Type: application/json" \
  -d '{"pickupLat":10.7626,"pickupLng":106.6601,"dropoffLat":10.7800,"dropoffLng":106.7000}'

# 3. check health từng service
curl http://localhost:8080/api/users/health      # (nếu UserService có expose health)
curl http://localhost:8080/api/trips/health
curl http://localhost:8080/api/drivers/health
```

Nếu các lệnh trên trả về 200 OK → stack sống. [Ok]
