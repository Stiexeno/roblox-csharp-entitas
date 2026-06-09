-- Per-type editor. Returns a row: label + editor input. onChange(newValue)
-- is called when the user commits (FocusLost for text fields, immediate
-- for checkbox).
--
-- Supported edit types:
--   boolean    → toggle button
--   number     → TextBox + tonumber parse on FocusLost
--   string     → TextBox + raw text on FocusLost
--   Vector3    → three side-by-side TextBoxes (X, Y, Z)
--   Color3     → hex TextBox "#rrggbb"; parses on FocusLost
--   table      → read-only "{...}" (drill via EntityDetail row click)
--   other      → read-only tostring

local function vector3ToText(v) return string.format("%.2f", v) end
local function color3ToHex(c)
	return string.format("#%02X%02X%02X",
		math.floor(c.R * 255 + 0.5),
		math.floor(c.G * 255 + 0.5),
		math.floor(c.B * 255 + 0.5))
end
local function parseHex(text)
	if text == nil then return nil end
	local h = string.gsub(text, "#", "")
	if #h ~= 6 then return nil end
	local r = tonumber("0x" .. string.sub(h, 1, 2))
	local g = tonumber("0x" .. string.sub(h, 3, 4))
	local b = tonumber("0x" .. string.sub(h, 5, 6))
	if r == nil or g == nil or b == nil then return nil end
	return Color3.fromRGB(r, g, b)
end

local function makeTextBox(create, text, width)
	return create("TextBox", {
		BackgroundColor3 = Color3.fromRGB(50, 50, 56),
		BorderSizePixel = 0,
		Size = UDim2.new(0, width or 100, 0, 20),
		Font = Enum.Font.Code,
		TextSize = 11,
		TextColor3 = Color3.fromRGB(220, 220, 220),
		ClearTextOnFocus = false,
		Text = text,
		TextXAlignment = Enum.TextXAlignment.Left,
		create("UICorner", { CornerRadius = UDim.new(0, 3) }),
		create("UIPadding", {
			PaddingLeft = UDim.new(0, 4),
			PaddingRight = UDim.new(0, 4),
		}),
	})
end

return function(debugger, label, value, onChange)
	local plasma = debugger.plasma
	local create = plasma.create
	local valueType = typeof(value)

	plasma.useInstance(function(ref)
		local row = create("Frame", {
			BackgroundColor3 = Color3.fromRGB(40, 40, 46),
			BorderSizePixel = 0,
			Size = UDim2.new(1, 0, 0, 24),
			create("UIListLayout", {
				FillDirection = Enum.FillDirection.Horizontal,
				VerticalAlignment = Enum.VerticalAlignment.Center,
				Padding = UDim.new(0, 6),
				SortOrder = Enum.SortOrder.LayoutOrder,
			}),
			create("UIPadding", {
				PaddingLeft = UDim.new(0, 8),
				PaddingRight = UDim.new(0, 8),
			}),
			create("TextLabel", {
				BackgroundTransparency = 1,
				Size = UDim2.new(0, 110, 1, 0),
				Font = Enum.Font.Gotham,
				TextSize = 11,
				TextColor3 = Color3.fromRGB(180, 180, 190),
				TextXAlignment = Enum.TextXAlignment.Left,
				Text = label,
				LayoutOrder = 1,
			}),
		})

		if valueType == "boolean" then
			local btn = create("TextButton", {
				Parent = row,
				BackgroundColor3 = value and Color3.fromRGB(80, 160, 100) or Color3.fromRGB(80, 80, 86),
				BorderSizePixel = 0,
				Size = UDim2.new(0, 28, 0, 16),
				Font = Enum.Font.GothamBold,
				TextSize = 10,
				TextColor3 = Color3.fromRGB(220, 220, 220),
				Text = value and "ON" or "OFF",
				LayoutOrder = 2,
				AutoButtonColor = false,
				create("UICorner", { CornerRadius = UDim.new(0, 3) }),
			})
			btn.MouseButton1Click:Connect(function()
				onChange(not value)
			end)

		elseif valueType == "number" then
			local box = makeTextBox(create, tostring(value), 120)
			box.Parent = row
			box.LayoutOrder = 2
			box.FocusLost:Connect(function()
				local n = tonumber(box.Text)
				if n ~= nil then onChange(n) else box.Text = tostring(value) end
			end)

		elseif valueType == "string" then
			local box = makeTextBox(create, value, 160)
			box.Parent = row
			box.LayoutOrder = 2
			box.FocusLost:Connect(function()
				onChange(box.Text)
			end)

		elseif valueType == "Vector3" then
			local boxes = {}
			for i, comp in ipairs({ value.X, value.Y, value.Z }) do
				local b = makeTextBox(create, vector3ToText(comp), 50)
				b.Parent = row
				b.LayoutOrder = 1 + i
				boxes[i] = b
			end
			for i, b in ipairs(boxes) do
				b.FocusLost:Connect(function()
					local x = tonumber(boxes[1].Text) or value.X
					local y = tonumber(boxes[2].Text) or value.Y
					local z = tonumber(boxes[3].Text) or value.Z
					onChange(Vector3.new(x, y, z))
				end)
			end

		elseif valueType == "Color3" then
			local box = makeTextBox(create, color3ToHex(value), 90)
			box.Parent = row
			box.LayoutOrder = 2
			box.FocusLost:Connect(function()
				local parsed = parseHex(box.Text)
				if parsed ~= nil then onChange(parsed) else box.Text = color3ToHex(value) end
			end)

		else
			create("TextLabel", {
				Parent = row,
				BackgroundTransparency = 1,
				Size = UDim2.new(1, -126, 1, 0),
				Font = Enum.Font.Code,
				TextSize = 10,
				TextColor3 = Color3.fromRGB(160, 160, 170),
				TextXAlignment = Enum.TextXAlignment.Left,
				Text = valueType == "table" and "{ ... }" or tostring(value),
				LayoutOrder = 2,
			})
		end

		return row
	end)
end
