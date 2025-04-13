# IPK Project 2: Client for a chat server using the IPK25-CHAT protocol 

**Author:** Filip Botlo  
**Login:** xbotlo01  
**Year:** 2025  

##  About

This project implements a command-line chat client supporting both **TCP** and **UDP** based on a custom IPK25-CHAT protocol.

The client can:
- Authenticate to the server using `/auth`
- Join a channel with `/join`
- Send messages to the channel
- Change display name using `/rename`
- Properly confirm and track messages (UDP only)

The client follows a **state machine**:  
`START → AUTH → OPEN → (ERROR or END)`

---

## 🚀 How to Run

### 🛠 Build
```bash
make




