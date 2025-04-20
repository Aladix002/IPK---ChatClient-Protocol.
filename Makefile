PROJECT_NAME = IPK25-CHAT
BUILD_DIR := bin
OUTPUT_NAME := IPK25-CHAT
CS_PROJ := IPK25-CHAT.csproj
LAUNCHER_NAME := ipk25-chat

all: build launcher

build:
	@dotnet build $(CS_PROJ) -o $(BUILD_DIR)

launcher:
	@echo 'dotnet $(BUILD_DIR)/$(OUTPUT_NAME).dll "$$@"' > $(LAUNCHER_NAME)
	@chmod +x $(LAUNCHER_NAME)

clean:
	rm -rf $(BUILD_DIR)
	rm -f $(LAUNCHER_NAME)
