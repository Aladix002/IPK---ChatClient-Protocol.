-- Protocol definition
local p_ipk = Proto("ipk25-chat", "IPK25 Chat");

-- Protocol configuration for dissector logic
local config = {
    debug                 = false,
    default_port          = 4567,
    tcp_terminal_bytes    = ByteArray.new("0D 0A"), -- \r\n
    message_concat_str    = " | ",
    client_to_server_str  = "C → Server",
    server_to_client_str  = "Server → C",
    direction_unknown_str = "? →      ?",
}

-- Message type enum
local MessageType = {
    Confirm      = 0,
    Reply        = 1,
    Authenticate = 2,
    JoinChannel  = 3,
    Message      = 4,
    Ping         = 0xFD,
    Error        = 0xFE,
    Disconnect   = 0xFF,
}

-- Message type enum to string mapping
local vs_messageType = {
    [MessageType.Confirm]      = "confirm",
    [MessageType.Ping]         = "ping",
    [MessageType.Reply]        = "reply",
    [MessageType.Authenticate] = "auth",
    [MessageType.JoinChannel]  = "join",
    [MessageType.Message]      = "msg",
    [MessageType.Error]        = "err",
    [MessageType.Disconnect]   = "bye",
}

-- Message type enum to string mapping
local vs_result = {
    [0] = "NOK",
    [1] = "OK",
}

-- Existing UDP Fields
local f_ip_proto = Field.new("ip.proto")
local f_udp_src  = Field.new("udp.srcport")
local f_udp_dst  = Field.new("udp.dstport")
local f_tcp_src  = Field.new("tcp.srcport")

-- Custom protocol fields
local f_type        = ProtoField.uint8("ipk25-chat.type", "Message Type", base.DEC, vs_messageType)
local f_id          = ProtoField.uint16("ipk25-chat.id", "Message ID", base.DEC)
local f_refId       = ProtoField.uint16("ipk25-chat.refId", "Reference Message ID", base.DEC)
local f_userName    = ProtoField.string("ipk25-chat.user-name", "User Name")
local f_displayName = ProtoField.string("ipk25-chat.display-name", "User Display Name")
local f_secret      = ProtoField.string("ipk25-chat.user-secret", "User Secret")
local f_channelId   = ProtoField.string("ipk25-chat.channel-id", "Channel ID")
local f_result      = ProtoField.uint8("ipk25-chat.result", "Result", base.DEC, vs_result)
local f_content     = ProtoField.string("ipk25-chat.content", "Content")

p_ipk.fields = { f_type, f_id, f_refId, f_userName, f_displayName, f_secret, f_channelId, f_result, f_content }

-- UDP message formatting helper
local Attrs = {}

function Attrs.new()
    local instance = {
        items = {},
        next_id = 1,
    }

    function instance.reset(self)
        self.next_id = 1
    end

    function instance.add(self, name, value, format)
        format = format or {}
        format.name = format.name or "%s"
        format.value = format.value or "%s"
        local format_string = string.format("%s=%s", format.name, format.value)
        self.items[self.next_id] = string.format(format_string, name, value)
        self.next_id = self.next_id + 1
    end

    function instance.concat(self, separator)
        if self.next_id == 1 then return "" end
        separator = separator or ", "

        local result = self.items[1]
        for _attr_id = 2, self.next_id - 1 do
            local attr = self.items[_attr_id]
            result = result .. separator .. attr
        end

        return result
    end

    return instance
end

-- simple substring search
function first_index_of(search, find)
    local matched_at = -1
    for search_idx = 0, search:len() - 1 do -- ByteArrays are indexed from 0, instead of standard 1 for Lua
        matched_at = search_idx
        for find_idx = 0, find:len() - 1 do -- ByteArrays are indexed from 0, instead of standard 1 for Lua
            local match_idx = search_idx + find_idx
            if match_idx >= search:len() or search:get_index(match_idx) ~= find:get_index(find_idx) then
                matched_at = -1
                break
            end
        end

        if matched_at >= 0 then break end
    end

    return matched_at
end

-- table with dissectors
local dissectors = {}

-- dissector method for a single TCP message
function dissectors.tcp_message(buffer, pinfo, tree, offset)
    local length = buffer:len()
    if length == 0 then return end
    offset = offset or 0
    local start_at = offset

    function read_message()
        -- use whole packet payload at first
        local message = buffer(offset):bytes()
        -- try getting index of terminal bytes for a TCP message
        local messageLength = first_index_of(message, config.tcp_terminal_bytes)
        if messageLength < 0 then
            -- terminal bytes were not found
            return nil
        end

        -- based on terminal bytes crop the resulting message
        message = buffer(offset, messageLength)
        offset = offset + messageLength + config.tcp_terminal_bytes:len()
        return message
    end

    -- try parsing a single message from the available stream
    local messageBuffer = read_message()
    if messageBuffer == nil then
        -- no whole message could be found
        return -1
    end

    -- a whole message was found
    local message = messageBuffer:string()
    local realMessageLength = offset - start_at

    -- Create protocol data subtree in packet inspection
    local subtree = tree:add(p_ipk, buffer(start_at, realMessageLength))

    -- Parse message type from content
    local messageType = -1
    if message:upper():find("^ERR FROM") then
        messageType = MessageType.Error

    elseif message:upper():find("^BYE FROM") then
        messageType = MessageType.Disconnect

    elseif message:upper():find("^MSG FROM") then
        messageType = MessageType.Message

    elseif message:upper():find("^JOIN") then
        messageType = MessageType.JoinChannel

    elseif message:upper():find("^AUTH") then
        messageType = MessageType.Authenticate

    elseif message:upper():find("^REPLY") then
        messageType = MessageType.Reply

    end

    subtree:add(f_type, messageType)
    subtree:add(f_content, messageBuffer)
    local messageTypeName = vs_messageType[messageType] or "unknown"

    -- Set protocol column value to protocol name
    pinfo.cols.info:append(config.message_concat_str .. message)

    subtree.text = string.format("IPK25-CHAT Protocol, Type: %s, Offset: %d, Length: %d", messageTypeName, start_at, realMessageLength)

    -- return number of consumed bytes
    return realMessageLength
end

-- dissector method for all TCP messages
-- based on https://gitlab.com/wireshark/wireshark/-/wikis/uploads/__moin_import__/attachments/Lua/Examples/fpm.lua
function dissectors.tcp(buffer, pinfo, tree)
    -- get the length of the packet buffer
    local packet_length = buffer:len()

    -- store the number of bytes successfully consumed
    local bytes_consumed = 0

    -- Set initial packet info string
    if f_tcp_src().value == config.default_port then
        pinfo.cols.info:append(config.server_to_client_str)
    else
        pinfo.cols.info:append(config.client_to_server_str)
    end

    -- process message parsing in a while loop, there could be multiple messages
    while bytes_consumed < packet_length do

        -- try to parse a single message from this packet
        local result = dissectors.tcp_message(buffer, pinfo, tree, bytes_consumed)

        if result > 0 then
            -- we successfully processed a message, of 'result' length
            bytes_consumed = bytes_consumed + result
            -- go again on another while loop

        elseif result < 0 then
            -- more bytes are needed to parse the whole message
            pinfo.desegment_offset = bytes_consumed
            pinfo.desegment_len = -result

            -- this whole packet has been successfully processed
            return packet_length

        else
            -- an error has occured while processing the packet
            return 0

        end
    end

    return bytes_consumed
end

-- table containing communicating parties
local parties = {
    clients = {},
    servers = {
        config.default_port,
    },
}

-- custom method for search in a table
function table.contains(t, item)
    for _, v in pairs(t) do
        if v == item then return true end
    end

    return false
end

-- dissector method for a single UDP message
function dissectors.udp(buffer, pinfo, tree)
    local length = buffer:len()
    if length == 0 then return end

    -- load data from UDP dissector
    local udp_src = f_udp_src().value
    local udp_dst = f_udp_dst().value

    if table.contains(parties.servers, udp_dst) then
        -- the destination port is a server, mark the source port as a client
        if table.contains(parties.clients, udp_src) == false then
            table.insert(parties.clients, udp_src)
        end
        pinfo.cols.info:append(config.client_to_server_str .. config.message_concat_str)

    elseif table.contains(parties.clients, udp_dst) then
        -- the destination port is a client, mark the source port as a server
        if table.contains(parties.servers, udp_src) == false then
            table.insert(parties.servers, udp_src)
        end
        pinfo.cols.info:append(config.server_to_client_str .. config.message_concat_str)

    else
        pinfo.cols.info:append(config.direction_unknown_str .. config.message_concat_str)

    end

    -- Add dissector to dynamic ports used in this communication
    add_dissector_to_udp_port(udp_src)

    -- Create protocol data subtree in packet inspection
    local subtree = tree:add(p_ipk, buffer())

    local offset = 0
    local bufferContent = buffer(offset)
    function read_stringz(protocol_field)
        local status, str = pcall(TvbRange.stringz, buffer:range(offset))
        if status ~= true or str == nil then
            return tostring(str)
        end

        bufferContent = buffer(offset, str:len())
        subtree:add(protocol_field, bufferContent)
        offset = offset + str:len() + 1
        return str
    end

    function read_buffer(length)
        local status, value = pcall(buffer, offset, length)
        if status ~= true or value == nil then
            return nil
        end

        bufferContent = value
        offset = offset + length
        return bufferContent
    end

    local attrs = Attrs.new()

    -- Extract message type
    read_buffer(1)
    subtree:add(f_type, bufferContent)
    local messageType = -1
    if bufferContent ~= nil then messageType = bufferContent:uint() end
    local messageTypeName = vs_messageType[messageType] or "unknown"

    -- Extract message ID
    read_buffer(2)
    local messageId = bufferContent:uint()

    -- Set default attributes
    if messageType ~= MessageType.Confirm then
        attrs:add("ID", messageId)
        subtree:add(f_id, bufferContent)
    end

    attrs:add("Type", messageTypeName)

    if messageType == MessageType.Confirm then
        attrs:add("RefID", messageId)
        subtree:add(f_refId, bufferContent)

    elseif messageType == MessageType.Error or messageType == MessageType.Message then
        local displayName = read_stringz(f_displayName)
        local content = read_stringz(f_content)
        attrs:add("DisplayName", displayName)
        attrs:add("Content", content)

    elseif messageType == MessageType.JoinChannel then
        local channelId = read_stringz(f_channelId)
        local displayName = read_stringz(f_displayName)
        attrs:add("ChannelId", channelId)
        attrs:add("DisplayName", displayName)

    elseif messageType == MessageType.Authenticate then
        local username = read_stringz(f_userName)
        local displayName = read_stringz(f_displayName)
        local secret = read_stringz(f_secret)
        attrs:add("UserName", username)
        attrs:add("DisplayName", displayName)
        attrs:add("Secret", secret)

    elseif messageType == MessageType.Reply then
        if read_buffer(1) ~= nil then
            subtree:add(f_result, bufferContent)
            attrs:add("Result", vs_result[bufferContent:uint()] or "unknown")
        end

        if read_buffer(2) ~= nil then
            subtree:add(f_refId, bufferContent)
            attrs:add("RefID", bufferContent:le_uint())
        end

        local content = read_stringz(f_content)
        attrs:add("Content", content)

    elseif messageType == MessageType.Disconnect then
        local displayName = read_stringz(f_displayName)
        attrs:add("DisplayName", displayName)

    end

    -- Concatenate defined attributes to a single string
    pinfo.cols.info:append(attrs:concat())

    subtree.text = string.format("IPK25-CHAT Protocol, ID: %d, Type: %s", messageId, messageTypeName)
end

-- Register protocol dissector
function p_ipk.dissector(buffer, pinfo, tree)
    local protocol_type = f_ip_proto().value

    -- Set protocol column value to protocol name
    pinfo.cols.protocol = p_ipk.name
    pinfo.cols.info = ""

    -- Decide which dissector to use based on transport layer protocol defined in IP header
    if protocol_type == 6 then
        -- TCP
        if config.debug then
            return dissectors.tcp(buffer, pinfo, tree)
        end

        local status, result_or_error = pcall(dissectors.tcp, buffer, pinfo, tree)
        if status ~= true then
            pinfo.cols.info:append(config.message_concat_str .. "Failed parsing: " .. result_or_error)
        end
        return result_or_error or 0

    elseif protocol_type == 17 then
        -- UDP
        if config.debug then
            return dissectors.udp(buffer, pinfo, tree)
        end

        local status, result_or_error = pcall(dissectors.udp, buffer, pinfo, tree)
        if status ~= true then
            pinfo.cols.info:append(config.message_concat_str .. "Failed parsing: " .. result_or_error)
        end
    end
end

function add_dissector_to_udp_port(port)
    -- register our protocol to handle dynamic port allocation
    local udp_table = DissectorTable.get("udp.port")
    udp_table:add(port, p_ipk)
end

-- Register for UDP on default protocol port
add_dissector_to_udp_port(config.default_port)

-- Register for TCP on default protocol port
local udp_table = DissectorTable.get("tcp.port")
udp_table:add(config.default_port, p_ipk)
