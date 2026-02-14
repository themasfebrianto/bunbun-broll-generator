# Components Refactoring Summary

This document describes the refactored Components structure for better maintainability and extensibility.

## 📁 New Folder Structure

```
Components/
├── _Imports.razor              # Global using statements
├── App.razor                   # Streamlined root component
├── Routes.razor                # Route definitions
├── CategorySelector.razor      # Shared form component
├── ShortVideoSettings.razor    # Shared form component
│
├── Common/                     # Reusable UI components
│   ├── UiConstants.cs          # CSS class and icon constants
│   ├── Icon.razor              # SVG icon component
│   ├── Button.razor            # Button component with variants
│   ├── Card.razor              # Card container component
│   ├── Badge.razor             # Badge/status component
│   └── FormField.razor         # Form field wrapper
│
├── Forms/                      # Form input components
│   ├── TextInput.razor         # Text input with validation
│   ├── TextArea.razor          # Textarea with validation
│   └── ToggleSwitch.razor      # Toggle/switch component
│
├── Layout/                     # Layout components (unchanged)
│   ├── AuthGuard.razor
│   ├── EmptyLayout.razor
│   ├── MainLayout.razor
│   ├── NavMenu.razor
│   ├── SignOutButton.razor
│   └── ThemeToggle.razor
│
├── Loading/                    # Loading/feedback components (unchanged)
│   ├── FullPageLoading.razor
│   ├── ProgressBar.razor
│   ├── StageIndicator.razor
│   ├── StickyProgress.razor
│   └── Toast.razor
│
├── Pages/                      # Page components (route handlers)
│   ├── About.razor
│   ├── Error.razor
│   ├── Home.razor              # Refactored - uses view components
│   ├── LoginPage.razor
│   ├── Projects.razor
│   ├── ScriptGenerator.razor   # Can be refactored similarly
│   └── VideoProjects.razor
│
└── Views/                      # Page-specific view components
    ├── Broll/                  # B-Roll page view components
    │   ├── BrollInputForm.razor
    │   ├── BrollResultsView.razor
    │   ├── BrollSidebar.razor
    │   └── SentenceCard.razor
    │
    └── ScriptGenerator/        # Script Generator view components
        ├── SessionListView.razor
        └── GenerationProgressView.razor

wwwroot/
├── css/
│   └── tailwind-config.css     # Extracted Tailwind/custom CSS
├── js/
│   ├── theme.js                # Theme toggle functionality
│   └── blazor-reconnect.js     # Blazor reconnection handling
└── app.css                     # App-specific styles (unchanged)
```

## 🔧 Key Improvements

### 1. **Extracted CSS & JavaScript**
- **Before**: All CSS and JS was inline in `App.razor` (514 lines)
- **After**: 
  - CSS moved to `wwwroot/css/tailwind-config.css`
  - Theme JS moved to `wwwroot/js/theme.js`
  - Blazor reconnection JS moved to `wwwroot/js/blazor-reconnect.js`
  - `App.razor` streamlined to ~120 lines

### 2. **Centralized UI Constants** (`Components/Common/UiConstants.cs`)
- Tailwind CSS class constants for consistency
- SVG icon path constants
- Common CSS class combinations
- Makes global style changes easier

### 3. **Reusable UI Components** (`Components/Common/`)

#### Icon Component
```razor
<Icon Name="spinner" Size="16" Class="animate-spin" />
<Icon Name="check" Size="20" />
```

#### Button Component
```razor
<Button Text="Save" Icon="save" OnClick="Save" />
<Button Text="Delete" Variant="destructive" IsLoading="_isDeleting" />
```

#### Card Component
```razor
<Card Title="Project Details" IsHoverable="true">
    <p>Content goes here</p>
</Card>
```

#### Badge Component
```razor
<Badge Variant="success">Completed</Badge>
<Badge Variant="primary" Icon="check">Ready</Badge>
```

### 4. **Form Components** (`Components/Forms/`)
- `TextInput` - With validation support
- `TextArea` - With resize options
- `ToggleSwitch` - Consistent toggle styling

### 5. **View Components** (`Components/Views/`)
Large page components are now split into focused view components:

#### Home.razor (B-Roll Generator)
**Before**: 1386 lines, mixed UI and logic
**After**: 
- `Home.razor` - ~500 lines, orchestrates view components
- `BrollInputForm.razor` - Input form UI
- `BrollResultsView.razor` - Results layout
- `BrollSidebar.razor` - Sidebar with stats/actions
- `SentenceCard.razor` - Individual sentence display

#### Benefits:
- Each component has a single responsibility
- Easier to test individual components
- UI changes are localized
- Can reuse view components in other pages

## 🎯 Usage Examples

### Using Common Components

```razor
@using BunbunBroll.Components.Common

<!-- Button with all features -->
<Button Text="Submit" 
        Icon="check" 
        Variant="primary"
        Size="lg"
        IsLoading="_isLoading"
        OnClick="HandleSubmit" />

<!-- Card with header and footer -->
<Card Title="Settings"
      HeaderActions="@HeaderTemplate"
      FooterContent="@FooterTemplate">
    <p>Card content here</p>
</Card>

<!-- Form field wrapper -->
<FormField Label="Email" 
           Description="We'll never share your email"
           Error="@_emailError"
           Required="true">
    <input type="email" class="input" @bind="_email" />
</FormField>
```

### Creating a New Page

1. Create a new `.razor` file in `Components/Pages/`
2. Use the `@page` directive for routing
3. Import common components: `@using BunbunBroll.Components.Common`
4. Use existing view components or create new ones in `Components/Views/`

### Creating a New View Component

1. Create a new `.razor` file in `Components/Views/{FeatureName}/`
2. Define parameters for data and callbacks
3. Use common UI components for consistency
4. Keep focused on a single view responsibility

Example:
```razor
@* Components/Views/MyFeature/MyView.razor *@
@namespace BunbunBroll.Components.Views.MyFeature

<div class="...">
    <Card Title="@Title">
        <Button Text="Action" OnClick="OnAction" />
    </Card>
</div>

@code {
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public EventCallback OnAction { get; set; }
}
```

## 📝 Guidelines

### When to Create a Component

1. **Common Components** (`Components/Common/`)
   - Reusable across multiple pages
   - Generic UI elements (buttons, cards, etc.)
   - Consistent styling needed

2. **View Components** (`Components/Views/{Feature}/`)
   - Page-specific UI sections
   - Complex UI that can be split
   - Potentially reusable within a feature area

3. **Form Components** (`Components/Forms/`)
   - Custom form inputs
   - Inputs with validation
   - Specialized editors

### Best Practices

1. **Use `UiConstants`** for Tailwind classes instead of hardcoding
2. **Use the `Icon` component** instead of inline SVG
3. **Keep components small** - max ~300 lines
4. **Use EventCallback** for parent communication
5. **Use parameters** for data flow (avoid cascading parameters when possible)
6. **Document public parameters** with XML comments

## 🔄 Migration Notes

### Existing Components
- `Loading/` and `Layout/` components are unchanged
- `Pages/` components can be gradually refactored
- New pages should follow the new structure

### Gradual Refactoring
1. Start using `Icon` and `Button` components in existing code
2. Extract repeated UI patterns into view components
3. Move inline styles to `UiConstants`
4. Split large page components last

## 🚀 Future Improvements

- Add unit tests for common components
- Create Storybook-style documentation
- Add more form components (Select, DatePicker, etc.)
- Create data table component
- Add animation/transition utilities
